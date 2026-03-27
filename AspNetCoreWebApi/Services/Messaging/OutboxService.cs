using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreWebApi.Data;
using AspNetCoreWebApi.Models.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// 发件箱服务 - 后台处理待发送消息，确保可靠投递
    /// </summary>
    public class OutboxService : BackgroundService
    {
        private readonly ILogger<OutboxService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMessageQueueService _messageQueue;

        // 配置
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);  // 轮询间隔
        private readonly int _batchSize = 100;  // 每批处理数量
        private readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(1);  // 重试延迟

        public OutboxService(
            ILogger<OutboxService> logger,
            IServiceScopeFactory scopeFactory,
            IMessageQueueService messageQueue)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _messageQueue = messageQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("发件箱服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingMessagesAsync(stoppingToken);
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发件箱处理消息时发生错误");
                    await Task.Delay(5000, stoppingToken);  // 错误后等待5秒
                }
            }

            _logger.LogInformation("发件箱服务已停止");
        }

        /// <summary>
        /// 处理待发送消息
        /// </summary>
        private async Task ProcessPendingMessagesAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 查询待发送消息
            var now = DateTime.UtcNow;
            var pendingMessages = await Task.Run(() => dbContext.OutboxMessages
                .Where(m => m.Status == OutboxMessageStatus.Pending ||
                           (m.Status == OutboxMessageStatus.Failed && 
                            m.RetryCount < m.MaxRetryCount && 
                            m.NextRetryAt <= now))
                .OrderBy(m => m.CreatedAt)
                .Take(_batchSize)
                .ToList(), stoppingToken);

            if (pendingMessages.Count == 0)
            {
                return;
            }

            _logger.LogInformation("处理待发送消息: {Count} 条", pendingMessages.Count);

            // 按队列分组批量发送
            var groupedMessages = pendingMessages.GroupBy(m => m.QueueName);
            var messagesToPublish = new List<byte[]>();
            var successIds = new List<string>();
            var failedMessages = new List<OutboxMessage>();

            foreach (var group in groupedMessages)
            {
                var queueName = group.Key;
                messagesToPublish.Clear();

                foreach (var msg in group)
                {
                    try
                    {
                        // 将 Base64 内容转换为字节数组
                        var messageBytes = Convert.FromBase64String(msg.Content);
                        messagesToPublish.Add(messageBytes);
                    }
                    catch (Exception ex)
                    {
                        msg.Status = OutboxMessageStatus.Failed;
                        msg.RetryCount++;
                        msg.NextRetryAt = DateTime.UtcNow.Add(_retryDelay);
                        msg.ErrorMessage = $"内容解析失败: {ex.Message}";
                        failedMessages.Add(msg);
                        _logger.LogError(ex, "消息内容解析失败: {MessageId}", msg.Id);
                    }
                }

                if (messagesToPublish.Count > 0)
                {
                    try
                    {
                        // 批量发送（带确认）
                        await _messageQueue.PublishBatchWithConfirmAsync(queueName, messagesToPublish);

                        // 标记成功
                        foreach (var msg in group)
                        {
                            if (!failedMessages.Contains(msg))
                            {
                                msg.Status = OutboxMessageStatus.Sent;
                                msg.SentAt = DateTime.UtcNow;
                                successIds.Add(msg.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 批量发送失败，标记重试
                        foreach (var msg in group)
                        {
                            if (!failedMessages.Contains(msg))
                            {
                                msg.Status = OutboxMessageStatus.Failed;
                                msg.RetryCount++;
                                msg.NextRetryAt = DateTime.UtcNow.Add(_retryDelay);
                                msg.ErrorMessage = $"发送失败: {ex.Message}";

                                if (msg.RetryCount >= msg.MaxRetryCount)
                                {
                                    msg.Status = OutboxMessageStatus.Cancelled;
                                    _logger.LogError("消息超过最大重试次数，已取消: {MessageId}", msg.Id);
                                }
                            }
                        }
                        _logger.LogError(ex, "批量发送消息失败: {QueueName}", queueName);
                    }
                }
            }

            // 保存状态更新
            await dbContext.SaveChangesAsync(stoppingToken);

            if (successIds.Count > 0)
            {
                _logger.LogInformation("成功发送消息: {Count} 条", successIds.Count);
            }
        }
    }
}
