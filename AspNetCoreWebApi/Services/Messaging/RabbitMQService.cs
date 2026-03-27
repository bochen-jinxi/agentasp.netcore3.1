using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// RabbitMQ消息队列服务接口 - 支持 MessagePack 二进制序列化
    /// </summary>
    public interface IMessageQueueService
    {
        /// <summary>
        /// 发布消息（二进制格式，支持 MessagePack）
        /// </summary>
        Task PublishAsync(string queueName, byte[] message);

        /// <summary>
        /// 批量发布消息（高性能）
        /// </summary>
        Task PublishBatchAsync(string queueName, IReadOnlyList<byte[]> messages);

        /// <summary>
        /// 批量发布消息（带确认，确保可靠投递）
        /// </summary>
        Task PublishBatchWithConfirmAsync(string queueName, IReadOnlyList<byte[]> messages);

        /// <summary>
        /// 发布消息（延迟，二进制格式）
        /// </summary>
        Task PublishDelayedAsync(string queueName, byte[] message, int delayMilliseconds);
    }

    /// <summary>
    /// RabbitMQ消息队列服务实现（高性能版本 - Channel通道池）
    /// </summary>
    public class RabbitMQService : IMessageQueueService, IDisposable
    {
        private readonly ILogger<RabbitMQService> _logger;
        private readonly RabbitMQConnectionManager _connectionManager;
        private readonly Channel<IModel> _channelPool;
        private readonly int _poolSize = 100; // 通道池大小
        private readonly bool _isEnabled;

        public RabbitMQService(RabbitMQConnectionManager connectionManager, ILogger<RabbitMQService> logger)
        {
            _logger = logger;
            _connectionManager = connectionManager;
            _channelPool = Channel.CreateBounded<IModel>(new BoundedChannelOptions(_poolSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });

            try
            {
                // 预先声明队列（幂等操作）
                _connectionManager.DeclareQueues("async_tasks", "batch_tasks", "batch_tasks_delayed", "order_events");
                
                // 创建通道池（使用连接管理器创建通道，带计数和限制）
                for (int i = 0; i < _poolSize; i++)
                {
                    _channelPool.Writer.TryWrite(_connectionManager.CreateChannel());
                }
                
                _isEnabled = true;
                _logger.LogInformation("RabbitMQ连接成功，Channel通道池大小: {PoolSize}", _poolSize);
            }
            catch (Exception ex)
            {
                _isEnabled = false;
                _logger.LogWarning(ex, "RabbitMQ连接失败，消息队列功能已禁用");
            }
        }

        public Task PublishAsync(string queueName, byte[] message)
        {
            if (!_isEnabled)
            {
                return Task.CompletedTask;
            }

            try
            {
                // 从池中获取通道，如果池为空则创建新通道
                IModel channel = null;
                bool fromPool = _channelPool.Reader.TryRead(out channel);
                
                if (!fromPool)
                {
                    // 池为空，创建临时通道
                    channel = _connectionManager.CreateChannel();
                }

                try
                {
                    // 直接使用二进制数据，无需编码转换
                    channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: message);
                }
                finally
                {
                    if (fromPool)
                    {
                        // 归还通道
                        _channelPool.Writer.TryWrite(channel);
                    }
                    else
                    {
                        // 临时通道直接关闭
                        channel.Close();
                        channel.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布消息失败: {QueueName}", queueName);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 批量发布消息（高性能，一次网络调用）
        /// </summary>
        public Task PublishBatchAsync(string queueName, IReadOnlyList<byte[]> messages)
        {
            if (!_isEnabled || messages.Count == 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                // 从池中获取通道，如果池为空则创建新通道
                IModel channel = null;
                bool fromPool = _channelPool.Reader.TryRead(out channel);
                
                if (!fromPool)
                {
                    // 池为空，创建临时通道
                    channel = _connectionManager.CreateChannel();
                }

                try
                {
                    // 使用 RabbitMQ 批量发布 API（最高效）
                    var batch = channel.CreateBasicPublishBatch();
                    
                    for (int i = 0; i < messages.Count; i++)
                    {
                        batch.Add(exchange: "", routingKey: queueName, mandatory: false, properties: null, body: messages[i]);
                    }
                    
                    // 一次性发送所有消息
                    batch.Publish();
                }
                finally
                {
                    if (fromPool)
                    {
                        _channelPool.Writer.TryWrite(channel);
                    }
                    else
                    {
                        channel.Close();
                        channel.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量发布消息失败: {QueueName}, 数量: {Count}", queueName, messages.Count);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 批量发布消息（带确认，确保可靠投递 - 适用于电商下单等关键业务）
        /// </summary>
        public Task PublishBatchWithConfirmAsync(string queueName, IReadOnlyList<byte[]> messages)
        {
            if (!_isEnabled || messages.Count == 0)
            {
                return Task.CompletedTask;
            }

            try
            {
                if (_channelPool.Reader.TryRead(out var channel))
                {
                    try
                    {
                        // 开启发布确认模式
                        channel.ConfirmSelect();

                        // 使用 RabbitMQ 批量发布 API
                        var batch = channel.CreateBasicPublishBatch();
                        
                        for (int i = 0; i < messages.Count; i++)
                        {
                            batch.Add(exchange: "", routingKey: queueName, mandatory: false, properties: null, body: messages[i]);
                        }
                        
                        // 一次性发送所有消息
                        batch.Publish();

                        // 等待确认（阻塞，确保消息已送达）
                        channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                    }
                    finally
                    {
                        _channelPool.Writer.TryWrite(channel);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量发布消息（带确认）失败: {QueueName}, 数量: {Count}", queueName, messages.Count);
                throw;  // 抛出异常，让调用方处理重试
            }

            return Task.CompletedTask;
        }

        public Task PublishDelayedAsync(string queueName, byte[] message, int delayMilliseconds)
        {
            if (!_isEnabled)
            {
                return Task.CompletedTask;
            }

            try
            {
                if (_channelPool.Reader.TryRead(out var channel))
                {
                    try
                    {
                        var delayedQueueName = $"{queueName}_delayed";
                        var properties = channel.CreateBasicProperties();
                        properties.Expiration = delayMilliseconds.ToString();
                        // 直接使用二进制数据，无需编码转换
                        channel.BasicPublish(exchange: "", routingKey: delayedQueueName, basicProperties: properties, body: message);
                    }
                    finally
                    {
                        _channelPool.Writer.TryWrite(channel);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布延迟消息失败: {QueueName}", queueName);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                while (_channelPool.Reader.TryRead(out var channel))
                {
                    channel?.Close();
                    channel?.Dispose();
                }
                // 连接由 RabbitMQConnectionManager 统一管理，这里不需要关闭
            }
            catch { }
        }
    }
}
