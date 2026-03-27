using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreWebApi.Controllers;
using AspNetCoreWebApi.Data;
using AspNetCoreWebApi.Models.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MessagePack;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// 订单事件消费者 - 微批量处理模式
    /// </summary>
    public class OrderEventConsumer : BackgroundService
    {
        private readonly ILogger<OrderEventConsumer> _logger;
        private readonly RabbitMQConnectionManager _connectionManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private IConnection _connection;
        private readonly List<IModel> _channels = new List<IModel>();

        // 微批量配置
        private readonly int _batchSize = 50;                              // 批量大小
        private readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(20);  // 批量窗口
        private readonly ConcurrentQueue<BatchItem> _batchQueue = new ConcurrentQueue<BatchItem>();
        private readonly int _prefetchCount = 100;  // 预取数（增大以支持批量）

        private int _processedCount = 0;

        public OrderEventConsumer(
            RabbitMQConnectionManager connectionManager,
            ILogger<OrderEventConsumer> logger,
            IServiceScopeFactory scopeFactory)
        {
            _connectionManager = connectionManager;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken);

            // 重试连接逻辑
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 使用连接管理器获取连接（单例）
                    _connection = _connectionManager.GetConnection();
                    break; // 连接成功，跳出重试循环
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取RabbitMQ连接失败，5秒后重试...");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (stoppingToken.IsCancellationRequested) return;

            try
            {

                // 创建通道消费消息（使用连接管理器创建通道）
                var channel = _connectionManager.CreateChannel();
                _channels.Add(channel);

                channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)_prefetchCount, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (model, ea) =>
                {
                    // 加入批量队列
                    _batchQueue.Enqueue(new BatchItem
                    {
                        DeliveryTag = ea.DeliveryTag,
                        Channel = channel,
                        Message = ea.Body.ToArray()
                    });
                };

                channel.BasicConsume(queue: "order_events", autoAck: false, consumer: consumer);

                _logger.LogInformation("订单事件消费者已启动 - 微批量模式, 批量大小: {BatchSize}, 批量窗口: {BatchWindow}ms",
                    _batchSize, _batchWindow.TotalMilliseconds);

                // 启动批量处理任务
                var batchProcessTask = ProcessBatchAsync(stoppingToken);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("订单事件消费者正在停止...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单事件消费者启动失败");
            }
            finally
            {
                // 处理剩余消息
                await FlushRemainingAsync();
            }
        }

        /// <summary>
        /// 批量处理循环
        /// </summary>
        private async Task ProcessBatchAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_batchWindow, stoppingToken);

                    var batch = new List<BatchItem>();
                    while (batch.Count < _batchSize && _batchQueue.TryDequeue(out var item))
                    {
                        batch.Add(item);
                    }

                    if (batch.Count == 0) continue;

                    await ProcessBatchItemsAsync(batch);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量处理订单事件失败");
                }
            }
        }

        /// <summary>
        /// 批量处理订单事件
        /// </summary>
        private async Task ProcessBatchItemsAsync(List<BatchItem> batch)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var orderIds = new List<string>();
            var events = new List<OrderCreatedEvent>();

            // 解析所有消息
            foreach (var item in batch)
            {
                try
                {
                    var orderEvent = MessagePackSerializer.Deserialize<OrderCreatedEvent>(
                        item.Message, MessagePackSerializerOptions.Standard);
                    if (orderEvent != null)
                    {
                        events.Add(orderEvent);
                        orderIds.Add(orderEvent.OrderId);
                        item.OrderEvent = orderEvent;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "反序列化订单事件失败");
                    item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: false);
                }
            }

            if (events.Count == 0) return;

            try
            {
                // 批量查询订单
                var orders = dbContext.Orders
                    .Where(o => orderIds.Contains(o.Id))
                    .ToDictionary(o => o.Id);

                // 批量更新订单状态
                foreach (var evt in events)
                {
                    if (orders.TryGetValue(evt.OrderId, out var order))
                    {
                        order.Status = OrderStatus.Processing;
                    }
                }

                // 批量保存
                await dbContext.SaveChangesAsync();

                // 批量确认消息
                foreach (var item in batch)
                {
                    if (item.OrderEvent != null)
                    {
                        try
                        {
                            item.Channel.BasicAck(deliveryTag: item.DeliveryTag, multiple: false);
                        }
                        catch { }
                    }
                }

                var count = Interlocked.Add(ref _processedCount, events.Count);
                if (count % 1000 == 0)
                {
                    _logger.LogInformation("已处理订单事件: {Count}, 本次批量: {BatchCount}", count, events.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量处理订单事件失败，数量: {Count}", events.Count);

                // 失败时逐条 Nack
                foreach (var item in batch)
                {
                    try
                    {
                        item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: true);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 处理剩余消息
        /// </summary>
        private async Task FlushRemainingAsync()
        {
            var remaining = new List<BatchItem>();
            while (_batchQueue.TryDequeue(out var item))
            {
                remaining.Add(item);
            }

            if (remaining.Count > 0)
            {
                _logger.LogInformation("处理剩余订单事件: {Count} 条", remaining.Count);
                await ProcessBatchItemsAsync(remaining);
            }
        }

        public override void Dispose()
        {
            try
            {
                foreach (var channel in _channels)
                {
                    channel?.Close();
                    channel?.Dispose();
                }
                _channels.Clear();

                _connection?.Close();
                _connection?.Dispose();
            }
            catch { }
            base.Dispose();
        }

        /// <summary>
        /// 批量项
        /// </summary>
        private class BatchItem
        {
            public ulong DeliveryTag { get; set; }
            public IModel Channel { get; set; }
            public byte[] Message { get; set; }
            public OrderCreatedEvent OrderEvent { get; set; }
        }
    }
}
