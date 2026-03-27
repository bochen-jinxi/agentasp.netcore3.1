using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// 响应缓存配置扩展类
    /// </summary>
    public static class CustomResponseCacheExtensions
    {
        /// <summary>
        /// 配置内存缓存和响应缓存
        /// </summary>
        public static IServiceCollection AddCustomResponseCaching(this IServiceCollection services)
        {
            services.AddMemoryCache(options =>
            {
                options.SizeLimit = 1024 * 1024 * 100; // 100MB缓存大小限制
            });

            services.AddResponseCaching(options =>
            {
                options.MaximumBodySize = 1024 * 1024; // 1MB响应体大小限制
                options.SizeLimit = 1024 * 1024 * 50; // 50MB总缓存大小
            });

            return services;
        }
    }
}
