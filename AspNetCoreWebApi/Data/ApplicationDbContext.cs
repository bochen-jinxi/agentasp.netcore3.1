using AspNetCoreWebApi.Models.Configurations;
using AspNetCoreWebApi.Models.Entities;
using AspNetCoreWebApi.Controllers;
using Microsoft.EntityFrameworkCore;

namespace AspNetCoreWebApi.Data
{
    /// <summary>
    /// 应用程序数据库上下文
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// 天气预报数据集
        /// </summary>
        public DbSet<WeatherForecast> WeatherForecasts { get; set; }

        /// <summary>
        /// 发件箱消息（本地消息表）
        /// </summary>
        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        /// <summary>
        /// 订单
        /// </summary>
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 应用实体配置
            modelBuilder.ApplyConfiguration(new WeatherForecastConfiguration());
        }
    }
}
