using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// Kestrel高性能配置扩展类
    /// </summary>
    public static class KestrelConfigurationExtensions
    {
        /// <summary>
        /// 配置Kestrel高性能参数
        /// </summary>
        public static IServiceCollection AddHighPerformanceKestrel(this IServiceCollection services)
        {
            services.Configure<KestrelServerOptions>(options =>
            {
                // 监听端口
                options.ListenAnyIP(5000);

                // 设置最大并发连接数（无限制）
                options.Limits.MaxConcurrentConnections = null;
                options.Limits.MaxConcurrentUpgradedConnections = null;

                // 设置请求体大小限制
                options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB

                // 设置请求头超时
                options.Limits.RequestHeadersTimeout = System.TimeSpan.FromSeconds(10);

                // 禁用最小数据速率限制（提高吞吐量）
                options.Limits.MinRequestBodyDataRate = null;
                options.Limits.MinResponseDataRate = null;

                // 保持连接活动状态
                options.Limits.KeepAliveTimeout = System.TimeSpan.FromMinutes(2);

                // 启用同步IO（某些场景下性能更好）
                options.AllowSynchronousIO = true;

                // 禁用响应压缩（减少CPU开销）
                options.AddServerHeader = false;
            });

            return services;
        }
    }
}
