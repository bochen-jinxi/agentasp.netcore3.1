using AspNetCoreWebApi.Data;
using AspNetCoreWebApi.Models.Entities;
using AspNetCoreWebApi.Repositories.Implementations;
using AspNetCoreWebApi.Repositories.Interfaces;
using AspNetCoreWebApi.Services.Implementations;
using AspNetCoreWebApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// 服务集合扩展类
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册应用程序服务
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // 注册数据库上下文
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            // 注册仓储
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            // 注册服务
            services.AddScoped<IWeatherService, WeatherService>();

            return services;
        }
    }
}
