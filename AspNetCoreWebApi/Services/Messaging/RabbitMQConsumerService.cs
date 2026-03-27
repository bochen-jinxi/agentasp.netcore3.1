using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreWebApi.Data;
using AspNetCoreWebApi.Models.Entities;
using AspNetCoreWebApi.Controllers; // 假设 AsyncProcessRequest 在这里
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
    /// RabbitMQ消息消费者后台服务 - 高性能优化版本（修复防丢失机制）
    /// </summary>
    public class RabbitMQConsumerService : BackgroundService
    {
        private readonly ILogger<RabbitMQConsumerService> _logger;
        private readonly RabbitMQConnectionManager _connectionManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private IConnection _connection;
        private readonly List<IModel> _channels = new List<IModel>();

        // 高并发配置
        private readonly int _channelCount = 50;
        private readonly int _prefetchCount = 50;

        // 批量处理配置 - 使用 BlockingCollection 确保数据安全
        private readonly BlockingCollection<BatchItem> _batchQueue = new BlockingCollection<BatchItem>(boundedCapacity: 100000);
        private readonly int _batchSize = 100;
        private readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(50);

        // 统计
        private static int _processedCount = 0;
        private static readonly Random _random = new Random();

        public RabbitMQConsumerService(
            RabbitMQConnectionManager connectionManager,
            ILogger<RabbitMQConsumerService> logger,
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

                // 启动任务
                var batchTasks = new Task[5];
                for (int i = 0; i < 5; i++)
                {
                    batchTasks[i] = Task.Run(() => BatchWriteToDatabaseAsync(stoppingToken), stoppingToken);
                }

                // 启动监控任务
                _ = Task.Run(async () =>
                {
                    // 等待一小段时间让任务启动
                    await Task.Delay(1000);

                    var runningCount = 0;
                    for (int i = 0; i < batchTasks.Length; i++)
                    {
                        if (batchTasks[i].Status == TaskStatus.Running ||
                            batchTasks[i].Status == TaskStatus.WaitingForActivation ||
                            batchTasks[i].Status == TaskStatus.WaitingToRun)
                        {
                            runningCount++;
                        }
                        else if (batchTasks[i].Status == TaskStatus.Faulted)
                        {
                            _logger.LogError(batchTasks[i].Exception, "任务 {Index} 启动失败", i + 1);
                        }
                    }

                    _logger.LogInformation("成功启动 {Count}/5 个任务", runningCount);
                }, stoppingToken);

                // 创建多个通道实现并发处理（使用连接管理器创建通道）
                for (int i = 0; i < _channelCount; i++)
                {
                    var channel = _connectionManager.CreateChannel();
                    _channels.Add(channel);

                    // 队列声明由 RabbitMQConnectionManager 统一处理（幂等）

                    channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)_prefetchCount, global: false);

                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.Received += async (model, ea) =>
                    {
                        var batchItem = new BatchItem
                        {
                            DeliveryTag = ea.DeliveryTag,
                            Channel = channel,
                            Message = ea.Body.ToArray()
                        };

                        // 加入批量队列（阻塞直到有空间）
                        _batchQueue.Add(batchItem, stoppingToken);
                        await Task.CompletedTask; // 显式消除 async 警告
                    };

                    channel.BasicConsume(queue: "async_tasks", autoAck: false, consumer: consumer);
                }

                _logger.LogInformation("RabbitMQ消费者服务已启动 - 通道数: {ChannelCount}, 预取数: {PrefetchCount}, 批量大小: {BatchSize}",
                    _channelCount, _prefetchCount, _batchSize);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("RabbitMQ消费者服务正在停止...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ消费者服务启动失败");
            }
            finally
            {
                // 服务停止时，处理剩余数据
                _batchQueue.CompleteAdding();
                await FlushRemainingDataAsync();
            }
        }

        /// <summary>
        /// 批量写入数据库（后台任务）- 确保数据安全
        /// </summary>
        private async Task BatchWriteToDatabaseAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ 任务已开始执行，线程ID: {ThreadId}", Thread.CurrentThread.ManagedThreadId);
            while (!stoppingToken.IsCancellationRequested && !_batchQueue.IsCompleted)
            {
                var batch = new List<BatchItem>();

                try
                {
                    var forecasts = new List<WeatherForecast>();

                    // 收集批量数据（带超时）
                    var timeoutTask = Task.Delay(_batchInterval, stoppingToken);

                    while (batch.Count < _batchSize && !_batchQueue.IsCompleted)
                    {
                        if (_batchQueue.TryTake(out var item, 0))
                        {
                            batch.Add(item);
                        }
                        else
                        {
                            break;
                        }
                    }

                    // 等待超时或批量满
                    await timeoutTask;

                    if (batch.Count == 0) continue;

                    // 处理消息
                    foreach (var item in batch)
                    {
                        try
                        {
                            var taskData = MessagePackSerializer.Deserialize<AsyncProcessRequest>(
                                item.Message, MessagePackSerializerOptions.Standard);

                            if (taskData != null)
                            {
                                forecasts.Add(new WeatherForecast
                                {
                                    Date = DateTime.UtcNow,
                                    TemperatureC = _random.Next(-20, 55),
                                    Summary = taskData.TaskType ?? "Processed"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "反序列化消息失败");
                            item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: false);
                        }
                    }

                    if (forecasts.Count > 0)
                    {
                        // 批量写入数据库
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        dbContext.WeatherForecasts.AddRange(forecasts);
                        await dbContext.SaveChangesAsync();

                        // 写入成功后，批量确认消息
                        foreach (var item in batch)
                        {
                            try
                            {
                                if (item.Channel.IsOpen)
                                {
                                    item.Channel.BasicAck(deliveryTag: item.DeliveryTag, multiple: false);
                                }
                            }
                            catch
                            {
                                // 通道可能已关闭
                            }
                        }

                        var count = Interlocked.Add(ref _processedCount, forecasts.Count);
                        if (count % 1000 == 0)
                        {
                            _logger.LogInformation("已处理消息: {Count}, 本次批量写入: {BatchCount}", count, forecasts.Count);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "批量写入数据库失败，准备将 {Count} 条消息重新退回队列", batch.Count);

                    // 数据库写入失败时，必须将消息重新入队
                    foreach (var item in batch)
                    {
                        try
                        {
                            if (item.Channel.IsOpen)
                            {
                                item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: true);
                            }
                        }
                        catch { }
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        /// <summary>
        /// 服务停止时，处理剩余数据
        /// </summary>
        private async Task FlushRemainingDataAsync()
        {
            _logger.LogInformation("正在处理剩余数据...");

            var remaining = new List<BatchItem>();
            while (_batchQueue.TryTake(out var item))
            {
                remaining.Add(item);
            }

            if (remaining.Count > 0)
            {
                _logger.LogInformation("剩余数据: {Count} 条", remaining.Count);

                try
                {
                    var forecasts = new List<WeatherForecast>();
                    foreach (var item in remaining)
                    {
                        try
                        {
                            var taskData = MessagePackSerializer.Deserialize<AsyncProcessRequest>(
                                item.Message, MessagePackSerializerOptions.Standard);

                            if (taskData != null)
                            {
                                forecasts.Add(new WeatherForecast
                                {
                                    Date = DateTime.UtcNow,
                                    TemperatureC = _random.Next(-20, 55),
                                    Summary = taskData.TaskType ?? "Processed"
                                });
                            }
                        }
                        catch
                        {
                            // 优雅停机阶段遇到反序列化错误，直接丢弃
                            item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: false);
                        }
                    }

                    if (forecasts.Count > 0)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        dbContext.WeatherForecasts.AddRange(forecasts);
                        await dbContext.SaveChangesAsync();

                        foreach (var item in remaining)
                        {
                            try { item.Channel.BasicAck(deliveryTag: item.DeliveryTag, multiple: false); } catch { }
                        }

                        _logger.LogInformation("剩余数据已写入: {Count} 条", forecasts.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理剩余数据写入数据库失败，将剩余消息退回队列");
                    // 🔥【核心修复点3】：优雅停机时如果写入失败，也要让消息回退
                    foreach (var item in remaining)
                    {
                        try { item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: true); } catch { }
                    }
                }
            }
        }

        public override void Dispose()
        {
            try
            {
                _batchQueue?.CompleteAdding();
                _batchQueue?.Dispose();

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
        /// 批量处理项
        /// </summary>
        private class BatchItem
        {
            public ulong DeliveryTag { get; set; }
            public IModel Channel { get; set; }
            public byte[] Message { get; set; }
        }
    }
}