using AspNetCoreWebApi.Services.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// 消息队列配置扩展类
    /// </summary>
    public static class MessageQueueExtensions
    {
        /// <summary>
        /// 配置RabbitMQ消息队列
        /// </summary>
        public static IServiceCollection AddMessageQueue(this IServiceCollection services, IConfiguration configuration)
        {
            // 注册连接管理器（单例，统一管理 RabbitMQ 连接）
            services.AddSingleton<RabbitMQConnectionManager>();
            
            services.AddSingleton<IMessageQueueService, RabbitMQService>();
            services.AddSingleton<IMicroBatchPublisher, MicroBatchPublisher>();
            // 启用消费者后台服务，将消息写入数据库
            services.AddHostedService<RabbitMQConsumerService>();
            // 启用发件箱服务，确保消息可靠投递
            services.AddHostedService<OutboxService>();
            // 启用微批量发布服务
            services.AddHostedService<MicroBatchPublisher>(sp => 
                (MicroBatchPublisher)sp.GetRequiredService<IMicroBatchPublisher>());
            // 启用订单事件消费者
            services.AddHostedService<OrderEventConsumer>();
            return services;
        }

        /// <summary>
        /// 初始化消息队列（确保队列在启动时创建）
        /// </summary>
        public static IApplicationBuilder UseMessageQueue(this IApplicationBuilder app)
        {
            // 强制初始化 RabbitMQService，确保队列在启动时创建
            var messageQueue = app.ApplicationServices.GetService(typeof(IMessageQueueService));
            return app;
        }
    }
}
