using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreWebApi.Controllers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// 缓冲区刷新服务 - 定期刷新消息缓冲区，确保不丢失消息
    /// </summary>
    public class BufferFlushService : BackgroundService
    {
        private readonly IMessageQueueService _messageQueue;
        private readonly ILogger<BufferFlushService> _logger;
        private readonly ConcurrentQueue<byte[]> _buffer;
        private readonly TimeSpan _flushInterval;
        private readonly object _lock;
        private DateTime _lastFlushTime;

        public BufferFlushService(
            IMessageQueueService messageQueue,
            ILogger<BufferFlushService> logger)
        {
            _messageQueue = messageQueue;
            _logger = logger;
            _buffer = HighPerformanceController.MessageBuffer;
            _flushInterval = TimeSpan.FromMilliseconds(100); // 每 100ms 检查一次
            _lock = HighPerformanceController.BatchLock;
            _lastFlushTime = DateTime.UtcNow;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("缓冲区刷新服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_flushInterval, stoppingToken);

                    var messagesToFlush = new List<byte[]>();

                    lock (_lock)
                    {
                        if (_buffer.Count > 0)
                        {
                            _lastFlushTime = DateTime.UtcNow;
                            while (_buffer.TryDequeue(out var msg))
                            {
                                messagesToFlush.Add(msg);
                            }
                        }
                    }

                    if (messagesToFlush.Count > 0)
                    {
                        await _messageQueue.PublishBatchAsync("async_tasks", messagesToFlush);
                        _logger.LogDebug("刷新缓冲区: {Count} 条消息", messagesToFlush.Count);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "刷新缓冲区失败");
                }
            }

            // 最后一次刷新
            await FlushRemainingAsync();
            _logger.LogInformation("缓冲区刷新服务已停止");
        }

        private async Task FlushRemainingAsync()
        {
            var messagesToFlush = new List<byte[]>();

            lock (_lock)
            {
                while (_buffer.TryDequeue(out var msg))
                {
                    messagesToFlush.Add(msg);
                }
            }

            if (messagesToFlush.Count > 0)
            {
                await _messageQueue.PublishBatchAsync("async_tasks", messagesToFlush);
                _logger.LogInformation("最后刷新缓冲区: {Count} 条消息", messagesToFlush.Count);
            }
        }
    }
}
