using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// 微批量发布服务接口 - 平衡实时性和批量效率
    /// </summary>
    public interface IMicroBatchPublisher
    {
        /// <summary>
        /// 发布消息（非阻塞，立即返回）
        /// </summary>
        ValueTask PublishAsync(string queueName, byte[] message);

        /// <summary>
        /// 发布消息并等待确认（关键业务用）
        /// </summary>
        Task PublishWithConfirmAsync(string queueName, byte[] message);
    }

    /// <summary>
    /// 微批量发布服务 - 适用于电商下单等实时性要求高的场景
    /// 特点：20ms 批量窗口，几乎实时，同时享受批量效率
    /// </summary>
    public class MicroBatchPublisher : IMicroBatchPublisher, IHostedService, IDisposable
    {
        private readonly Channel<BatchItem> _channel;
        private readonly IMessageQueueService _messageQueue;
        private readonly ILogger<MicroBatchPublisher> _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // 微批量配置
        private readonly int _batchSize = 50;                              // 批量大小
        private readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(20);  // 批量窗口（20ms）
        private readonly TimeSpan _maxWaitTime = TimeSpan.FromMilliseconds(100); // 最大等待时间

        private int _processedCount = 0;
        private int _batchCount = 0;

        public MicroBatchPublisher(
            IMessageQueueService messageQueue,
            ILogger<MicroBatchPublisher> logger)
        {
            _channel = Channel.CreateBounded<BatchItem>(new BoundedChannelOptions(100000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _messageQueue = messageQueue;
            _logger = logger;
        }

        /// <summary>
        /// 发布消息（非阻塞，立即返回）
        /// </summary>
        public async ValueTask PublishAsync(string queueName, byte[] message)
        {
            await _channel.Writer.WriteAsync(new BatchItem
            {
                QueueName = queueName,
                Message = message
            });
        }

        /// <summary>
        /// 发布消息并等待确认（关键业务用）
        /// </summary>
        public async Task PublishWithConfirmAsync(string queueName, byte[] message)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _channel.Writer.WriteAsync(new BatchItem
            {
                QueueName = queueName,
                Message = message,
                ConfirmSource = tcs
            });
            await tcs.Task;  // 等待确认
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("微批量发布服务已启动 - 批量大小: {BatchSize}, 批量窗口: {BatchWindow}ms",
                _batchSize, _batchWindow.TotalMilliseconds);

            _ = ProcessBatchAsync(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("微批量发布服务正在停止...");
            _cts.Cancel();

            // 刷新剩余消息
            await FlushRemainingAsync();

            _logger.LogInformation("微批量发布服务已停止 - 总处理: {ProcessedCount}, 总批次: {BatchCount}",
                _processedCount, _batchCount);
        }

        /// <summary>
        /// 批量处理循环
        /// </summary>
        private async Task ProcessBatchAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ProcessOneBatchAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量处理错误");
                    await Task.Delay(100, ct);
                }
            }
        }

        /// <summary>
        /// 处理一个批次
        /// </summary>
        private async Task ProcessOneBatchAsync(CancellationToken ct)
        {
            // 等待第一条消息
            if (await _channel.Reader.WaitToReadAsync(ct))
            {
                var startTime = DateTime.UtcNow;
                var batch = new List<BatchItem>();
                var byQueue = new Dictionary<string, List<BatchItem>>();

                // 收集消息（窗口内或达到批量大小）
                while ((DateTime.UtcNow - startTime) < _batchWindow && batch.Count < _batchSize)
                {
                    if (_channel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);

                        if (!byQueue.ContainsKey(item.QueueName))
                            byQueue[item.QueueName] = new List<BatchItem>();
                        byQueue[item.QueueName].Add(item);
                    }
                    else
                    {
                        // 检查是否超过最大等待时间
                        if ((DateTime.UtcNow - startTime) >= _maxWaitTime)
                            break;

                        await Task.Delay(1, ct);
                    }
                }

                if (batch.Count == 0)
                    return;

                // 按队列批量发送
                var allSuccess = true;
                foreach (var kvp in byQueue)
                {
                    var queueName = kvp.Key;
                    var items = kvp.Value;
                    var messages = new List<byte[]>();

                    foreach (var item in items)
                        messages.Add(item.Message);

                    try
                    {
                        // 批量发送（带确认）
                        await _messageQueue.PublishBatchWithConfirmAsync(queueName, messages);

                        // 通知等待者成功
                        foreach (var item in items)
                        {
                            item.ConfirmSource?.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        allSuccess = false;
                        _logger.LogError(ex, "批量发送失败: {QueueName}, 数量: {Count}", queueName, messages.Count);

                        // 通知等待者失败
                        foreach (var item in items)
                        {
                            item.ConfirmSource?.TrySetException(ex);
                        }
                    }
                }

                // 更新统计
                var processed = Interlocked.Add(ref _processedCount, batch.Count);
                Interlocked.Increment(ref _batchCount);

                // 定期输出统计
                if (processed % 1000 == 0)
                {
                    _logger.LogInformation("微批量发布统计 - 已处理: {ProcessedCount}, 本批次: {BatchCount}",
                        processed, batch.Count);
                }
            }
        }

        /// <summary>
        /// 刷新剩余消息
        /// </summary>
        private async Task FlushRemainingAsync()
        {
            _logger.LogInformation("正在刷新剩余消息...");

            var remaining = new List<BatchItem>();
            while (_channel.Reader.TryRead(out var item))
            {
                remaining.Add(item);
            }

            if (remaining.Count > 0)
            {
                _logger.LogInformation("剩余消息: {Count} 条", remaining.Count);

                var byQueue = new Dictionary<string, List<BatchItem>>();
                foreach (var item in remaining)
                {
                    if (!byQueue.ContainsKey(item.QueueName))
                        byQueue[item.QueueName] = new List<BatchItem>();
                    byQueue[item.QueueName].Add(item);
                }

                foreach (var kvp in byQueue)
                {
                    var messages = new List<byte[]>();
                    foreach (var item in kvp.Value)
                        messages.Add(item.Message);

                    try
                    {
                        await _messageQueue.PublishBatchWithConfirmAsync(kvp.Key, messages);
                        foreach (var item in kvp.Value)
                            item.ConfirmSource?.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "刷新剩余消息失败: {QueueName}", kvp.Key);
                        foreach (var item in kvp.Value)
                            item.ConfirmSource?.TrySetException(ex);
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }

        /// <summary>
        /// 批量项
        /// </summary>
        private class BatchItem
        {
            public string QueueName { get; set; }
            public byte[] Message { get; set; }
            public TaskCompletionSource<bool> ConfirmSource { get; set; }
        }
    }
}
