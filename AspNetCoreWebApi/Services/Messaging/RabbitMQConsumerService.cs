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
        private static int _ackedCount = 0;
        private static int _nackedCount = 0;
        private static int _receivedCount = 0;
        private static readonly Random _random = new Random();
        private static readonly object _logLock = new object();
        private static readonly string _logFile = "consumer_debug.log";
        
        // 详细日志
        private void LogDebug(string message)
        {
            var logMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [Thread:{Thread.CurrentThread.ManagedThreadId}] {message}";
            _logger.LogInformation(message);
            try
            {
                lock (_logLock)
                {
                    System.IO.File.AppendAllText(_logFile, logMessage + Environment.NewLine);
                }
            }
            catch { }
        }

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
                        var receivedNum = Interlocked.Increment(ref _receivedCount);
                        LogDebug($"[RECEIVE] 消息到达 #{receivedNum}, DeliveryTag={ea.DeliveryTag}");
                        
                        var batchItem = new BatchItem
                        {
                            DeliveryTag = ea.DeliveryTag,
                            Channel = channel,
                            Message = ea.Body.ToArray(),
                            SequenceNumber = receivedNum
                        };

                        // 加入批量队列
                        try
                        {
                            _batchQueue.Add(batchItem, stoppingToken);
                            LogDebug($"[QUEUE] 消息入队 #{receivedNum}, DeliveryTag={ea.DeliveryTag}, QueueCount={_batchQueue.Count}");
                        }
                        catch (OperationCanceledException)
                        {
                            LogDebug($"[CANCEL] 应用停止，消息未入队 #{receivedNum}, DeliveryTag={ea.DeliveryTag}");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[ERROR] 加入队列失败 #{receivedNum}, DeliveryTag={ea.DeliveryTag}, Error={ex.Message}");
                        }
                        await Task.CompletedTask;
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
                var successItems = new List<BatchItem>();  // 移到 try 外部，catch 可以访问

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
                    
                    LogDebug($"[BATCH] 开始处理批次, 消息数={batch.Count}, 序号范围=#{batch[0].SequenceNumber}-#{batch[batch.Count-1].SequenceNumber}");

                    // 处理消息，记录成功处理的消息
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
                                successItems.Add(item);  // 记录成功处理的消息
                            }
                            else
                            {
                                // 空消息，直接确认（不写入数据库，但算处理成功）
                                LogDebug($"[EMPTY] 空消息 #{item.SequenceNumber}, DeliveryTag={item.DeliveryTag}");
                                TryAckMessage(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[DESERIALIZE_FAIL] 反序列化失败 #{item.SequenceNumber}, DeliveryTag={item.DeliveryTag}, Error={ex.Message}");
                            TryNackMessage(item, requeue: false);  // 脏数据，丢弃
                        }
                    }

                    if (forecasts.Count > 0)
                    {
                        // 批量写入数据库
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        dbContext.WeatherForecasts.AddRange(forecasts);
                        await dbContext.SaveChangesAsync();

                        // 写入成功后，只确认成功处理的消息
                        foreach (var item in successItems)
                        {
                            LogDebug($"[ACK] 确认消息 #{item.SequenceNumber}, DeliveryTag={item.DeliveryTag}");
                            TryAckMessage(item);
                        }

                        var count = Interlocked.Add(ref _processedCount, forecasts.Count);
                        LogDebug($"[BATCH_DONE] 批量完成, 写入={forecasts.Count}, 累计={count}, Ack={_ackedCount}, Nack={_nackedCount}");
                    }
                    else if (successItems.Count > 0)
                    {
                        // forecasts.Count == 0 但 successItems.Count > 0 不应该发生
                        LogDebug($"[WARN] 异常: forecasts=0 但 successItems={successItems.Count}");
                        foreach (var item in successItems)
                        {
                            TryAckMessage(item);
                        }
                    }
                    else if (batch.Count > 0)
                    {
                        // batch 有消息但 forecasts 和 successItems 都为空
                        LogDebug($"[BATCH_EMPTY] 批次无数据写入, batch={batch.Count}");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"[DB_ERROR] 批量写入数据库失败，准备将 {successItems.Count} 条消息重新退回队列，错误: {ex.Message}");

                    // 数据库写入失败时，只对 successItems 进行 Nack requeue
                    // 其他消息（反序列化失败）已经 Nack 过了
                    foreach (var item in successItems)
                    {
                        TryNackMessage(item, requeue: true);
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        /// <summary>
        /// 安全确认消息
        /// </summary>
        private void TryAckMessage(BatchItem item)
        {
            try
            {
                if (item.Channel == null)
                {
                    _logger.LogWarning("Ack 失败: 通道为 null, DeliveryTag={DeliveryTag}", item.DeliveryTag);
                    return;
                }
                if (!item.Channel.IsOpen)
                {
                    _logger.LogWarning("Ack 失败: 通道已关闭, DeliveryTag={DeliveryTag}", item.DeliveryTag);
                    return;
                }
                item.Channel.BasicAck(deliveryTag: item.DeliveryTag, multiple: false);
                Interlocked.Increment(ref _ackedCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ack 消息失败: {DeliveryTag}", item.DeliveryTag);
            }
        }

        /// <summary>
        /// 安全拒绝消息
        /// </summary>
        private void TryNackMessage(BatchItem item, bool requeue)
        {
            try
            {
                if (item.Channel == null)
                {
                    LogDebug($"[NACK_FAIL] 通道为 null, DeliveryTag={item.DeliveryTag}, Seq=#{item.SequenceNumber}");
                    return;
                }
                if (!item.Channel.IsOpen)
                {
                    LogDebug($"[NACK_FAIL] 通道已关闭, DeliveryTag={item.DeliveryTag}, Seq=#{item.SequenceNumber}, requeue={requeue}");
                    return;
                }
                item.Channel.BasicNack(deliveryTag: item.DeliveryTag, multiple: false, requeue: requeue);
                Interlocked.Increment(ref _nackedCount);
                LogDebug($"[NACK] Nack 消息, DeliveryTag={item.DeliveryTag}, Seq=#{item.SequenceNumber}, requeue={requeue}");
            }
            catch (Exception ex)
            {
                LogDebug($"[NACK_ERROR] Nack 异常, DeliveryTag={item.DeliveryTag}, Seq=#{item.SequenceNumber}, Error={ex.Message}");
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

                var forecasts = new List<WeatherForecast>();
                var successItems = new List<BatchItem>();
                
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
                            successItems.Add(item);
                        }
                        else
                        {
                            // 空消息，直接确认
                            TryAckMessage(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "优雅停机阶段反序列化失败，消息丢弃: {DeliveryTag}", item.DeliveryTag);
                        TryNackMessage(item, requeue: false);
                    }
                }

                if (forecasts.Count > 0)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        dbContext.WeatherForecasts.AddRange(forecasts);
                        await dbContext.SaveChangesAsync();

                        // 只确认成功处理的消息
                        foreach (var item in successItems)
                        {
                            TryAckMessage(item);
                        }

                        _logger.LogInformation("剩余数据已写入: {Count} 条", forecasts.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理剩余数据写入数据库失败，将消息退回队列");
                        foreach (var item in successItems)
                        {
                            TryNackMessage(item, requeue: true);
                        }
                    }
                }
                else if (successItems.Count > 0)
                {
                    // 不应该发生，但为了安全
                    foreach (var item in successItems)
                    {
                        TryAckMessage(item);
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
            public int SequenceNumber { get; set; }  // 用于追踪
        }
    }
}