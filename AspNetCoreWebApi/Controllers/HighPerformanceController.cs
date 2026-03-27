using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetCoreWebApi.DTOs.Responses;
using AspNetCoreWebApi.Services.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using MessagePack;

namespace AspNetCoreWebApi.Controllers
{
    /// <summary>
    /// 高性能API控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class HighPerformanceController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IDistributedCache _distributedCache;
        private readonly IMessageQueueService _messageQueue;
        private static int _requestCount = 0;

        // 批量发布缓冲区 - 收集消息后批量发送
        private static readonly ConcurrentQueue<byte[]> _messageBuffer = new ConcurrentQueue<byte[]>();
        private static readonly int _batchSize = 100;  // 批量大小
        private static readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(50);  // 批量间隔
        private static DateTime _lastBatchTime = DateTime.UtcNow;
        private static readonly object _batchLock = new object();

        // 暴露给 BufferFlushService
        public static ConcurrentQueue<byte[]> MessageBuffer => _messageBuffer;
        public static object BatchLock => _batchLock;

        public HighPerformanceController(
            IMemoryCache memoryCache,
            IDistributedCache distributedCache,
            IMessageQueueService messageQueue)
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _messageQueue = messageQueue;
        }

        /// <summary>
        /// 高性能Ping测试（内存缓存）
        /// </summary>
        [HttpGet("ping")]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
        public IActionResult Ping()
        {
            return Ok(ApiResponse<string>.Ok("Pong", "Ping成功"));
        }

        /// <summary>
        /// 高性能计数器（内存缓存）
        /// </summary>
        [HttpGet("counter")]
        public IActionResult GetCounter()
        {
            var cacheKey = "request_counter";

            if (!_memoryCache.TryGetValue(cacheKey, out int counter))
            {
                counter = System.Threading.Interlocked.Increment(ref _requestCount);
                _memoryCache.Set(cacheKey, counter, TimeSpan.FromMinutes(1));
            }

            return Ok(ApiResponse<int>.Ok(counter, "请求计数"));
        }

        /// <summary>
        /// 分布式缓存测试
        /// </summary>
        [HttpGet("distributed-cache")]
        public async Task<IActionResult> GetDistributedCache()
        {
            var cacheKey = "distributed_test";
            var cachedValue = await _distributedCache.GetStringAsync(cacheKey);

            if (string.IsNullOrEmpty(cachedValue))
            {
                cachedValue = DateTime.UtcNow.ToString("O");
                await _distributedCache.SetStringAsync(cacheKey, cachedValue, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }

            return Ok(ApiResponse<string>.Ok(cachedValue, "分布式缓存测试"));
        }

        /// <summary>
        /// 消息队列测试（异步处理）- MessagePack 高性能版本
        /// 单个请求，内部使用批量发布优化
        /// </summary>
        [HttpPost("async-process")]
        public async Task<IActionResult> AsyncProcess([FromBody] AsyncProcessRequest request)
        {
            // 使用 MessagePack 序列化（比 JSON 快 10-100 倍，体积更小）
            var message = MessagePackSerializer.Serialize(request, MessagePackSerializerOptions.Standard);

            // 检查是否需要批量发送
            var messagesToFlush = new List<byte[]>();

            lock (_batchLock)
            {
                // 加入缓冲区
                _messageBuffer.Enqueue(message);
                
                var now = DateTime.UtcNow;
                var bufferCount = _messageBuffer.Count;

                // 条件1：缓冲区达到批量大小
                // 条件2：超过时间间隔且缓冲区有数据
                if (bufferCount >= _batchSize || 
                    (bufferCount > 0 && (now - _lastBatchTime) >= _batchInterval))
                {
                    _lastBatchTime = now;

                    // 取出所有消息（确保不丢失）
                    while (_messageBuffer.TryDequeue(out var msg))
                    {
                        messagesToFlush.Add(msg);
                    }
                }
            }

            // 批量发送（在 lock 外执行，避免阻塞）
            if (messagesToFlush.Count > 0)
            {
                await _messageQueue.PublishBatchAsync("async_tasks", messagesToFlush).ConfigureAwait(false);
            }

            return Accepted(ApiResponse<string>.Ok("任务已提交", "异步处理中"));
        }

        /// <summary>
        /// 批量处理（削峰）- MessagePack 高性能版本
        /// </summary>
        [HttpPost("batch")]
        public async Task<IActionResult> BatchProcess([FromBody] BatchProcessRequest request)
        {
            // 使用 MessagePack 序列化（比 JSON 快 10-100 倍，体积更小）
            var message = MessagePackSerializer.Serialize(request, MessagePackSerializerOptions.Standard);
            
            // 将批量任务放入延迟队列，实现削峰
            // 使用 ConfigureAwait(false) 提升性能，同时确保消息可靠性
            await _messageQueue.PublishDelayedAsync("batch_tasks", message, 100).ConfigureAwait(false);

            return Accepted(ApiResponse<string>.Ok("批量任务已提交", "延迟处理中"));
        }
    }

    /// <summary>
    /// 异步处理请求 - MessagePack 优化
    /// </summary>
    [MessagePackObject]
    public class AsyncProcessRequest
    {
        [Key(0)]
        public string TaskType { get; set; }
        [Key(1)]
        public object Data { get; set; }
    }

    /// <summary>
    /// 批量处理请求 - MessagePack 优化
    /// </summary>
    [MessagePackObject]
    public class BatchProcessRequest
    {
        [Key(0)]
        public string BatchId { get; set; }
        [Key(1)]
        public object[] Items { get; set; }
    }
}
