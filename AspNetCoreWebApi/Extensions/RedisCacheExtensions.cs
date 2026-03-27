using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// Redis分布式缓存配置扩展类
    /// </summary>
    public static class RedisCacheExtensions
    {
        /// <summary>
        /// 配置Redis分布式缓存
        /// </summary>
        public static IServiceCollection AddDistributedRedisCache(this IServiceCollection services, IConfiguration configuration)
        {
            var redisConnection = configuration.GetConnectionString("Redis");

            if (!string.IsNullOrEmpty(redisConnection))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnection;
                    options.InstanceName = "AspNetCoreWebApi_";
                });
            }
            else
            {
                // 如果没有配置Redis，使用内存分布式缓存作为替代
                services.AddDistributedMemoryCache();
            }

            return services;
        }
    }
}
