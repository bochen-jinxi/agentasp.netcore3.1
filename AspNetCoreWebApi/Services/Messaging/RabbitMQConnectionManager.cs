using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AspNetCoreWebApi.Services.Messaging
{
    /// <summary>
    /// RabbitMQ 连接管理器 - 单例模式，统一管理连接
    /// 修复高并发下的问题：
    /// 1. 连接断开后自动重连
    /// 2. 通道数限制管理
    /// 3. 线程安全的连接获取
    /// </summary>
    public class RabbitMQConnectionManager : IDisposable
    {
        private readonly ILogger<RabbitMQConnectionManager> _logger;
        private readonly IConfiguration _configuration;
        private IConnection _connection;
        private readonly object _lock = new object();
        private bool _disposed = false;
        
        // 通道计数器，用于监控
        private int _channelCount = 0;
        private readonly int _maxChannelsPerConnection = 2000; // RabbitMQ 默认每个连接最多 2047 个通道

        public RabbitMQConnectionManager(IConfiguration configuration, ILogger<RabbitMQConnectionManager> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 获取 RabbitMQ 连接（单例，线程安全，支持自动重连）
        /// </summary>
        public IConnection GetConnection()
        {
            // 快速路径：连接存在且打开
            var connection = _connection;
            if (connection != null && connection.IsOpen)
            {
                return connection;
            }

            // 慢速路径：需要创建或重建连接
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(RabbitMQConnectionManager));
                }

                // 双重检查
                connection = _connection;
                if (connection != null && connection.IsOpen)
                {
                    return connection;
                }

                // 清理旧连接
                if (connection != null)
                {
                    try
                    {
                        connection.Close(TimeSpan.FromSeconds(1));
                        connection.Dispose();
                        _logger.LogWarning("已清理断开的旧连接");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "清理旧连接时出错");
                    }
                }

                // 创建新连接
                _connection = CreateNewConnection();
                _channelCount = 0; // 重置通道计数
                
                return _connection;
            }
        }

        /// <summary>
        /// 创建新连接
        /// </summary>
        private IConnection CreateNewConnection()
        {
            var rabbitMqConnection = _configuration.GetConnectionString("RabbitMQ");

            if (string.IsNullOrEmpty(rabbitMqConnection))
            {
                throw new InvalidOperationException("RabbitMQ连接字符串未配置");
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitMqConnection),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                SocketReadTimeout = TimeSpan.FromSeconds(3),
                SocketWriteTimeout = TimeSpan.FromSeconds(3),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                DispatchConsumersAsync = true,
                ConsumerDispatchConcurrency = 4
            };

            var connection = factory.CreateConnection();
            _logger.LogInformation("RabbitMQ连接已创建");

            // 监听连接关闭事件
            connection.ConnectionShutdown += (sender, args) =>
            {
                _logger.LogWarning("RabbitMQ连接关闭: {Reason}", args.ReplyText);
            };

            return connection;
        }

        /// <summary>
        /// 创建新通道（带计数和限制）
        /// </summary>
        public IModel CreateChannel()
        {
            var connection = GetConnection();
            
            // 检查通道数限制
            var currentCount = Interlocked.Increment(ref _channelCount);
            if (currentCount > _maxChannelsPerConnection)
            {
                Interlocked.Decrement(ref _channelCount);
                throw new InvalidOperationException($"通道数超过限制: {_maxChannelsPerConnection}。请考虑减少并发或增加连接。");
            }

            try
            {
                var channel = connection.CreateModel();
                
                // 通道关闭时减少计数
                channel.ModelShutdown += (sender, args) =>
                {
                    Interlocked.Decrement(ref _channelCount);
                };

                return channel;
            }
            catch
            {
                Interlocked.Decrement(ref _channelCount);
                throw;
            }
        }

        /// <summary>
        /// 获取当前通道数（用于监控）
        /// </summary>
        public int CurrentChannelCount => _channelCount;

        /// <summary>
        /// 声明队列（幂等操作）
        /// </summary>
        public void DeclareQueue(string queueName, bool durable = true, bool exclusive = false, bool autoDelete = false)
        {
            using var channel = CreateChannel();
            channel.QueueDeclare(queue: queueName, durable: durable, exclusive: exclusive, autoDelete: autoDelete, arguments: null);
            _logger.LogInformation("队列已声明: {QueueName}", queueName);
        }

        /// <summary>
        /// 批量声明队列
        /// </summary>
        public void DeclareQueues(params string[] queueNames)
        {
            using var channel = CreateChannel();
            foreach (var queueName in queueNames)
            {
                channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            }
            _logger.LogInformation("队列已声明: {Queues}", string.Join(", ", queueNames));
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try
            {
                _connection?.Close(TimeSpan.FromSeconds(5));
                _connection?.Dispose();
                _logger.LogInformation("RabbitMQ连接已关闭");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭RabbitMQ连接失败");
            }
        }
    }
}
