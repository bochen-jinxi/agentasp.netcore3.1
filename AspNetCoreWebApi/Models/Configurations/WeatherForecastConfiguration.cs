using AspNetCoreWebApi.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspNetCoreWebApi.Models.Configurations
{
    /// <summary>
    /// WeatherForecast 实体配置类
    /// </summary>
    public class WeatherForecastConfiguration : IEntityTypeConfiguration<WeatherForecast>
    {
        public void Configure(EntityTypeBuilder<WeatherForecast> builder)
        {
            // 配置表名
            builder.ToTable("WeatherForecasts");

            // 配置主键
            builder.HasKey(w => w.Id);

            // 配置Id为string类型
            builder.Property(w => w.Id)
                .HasMaxLength(50)
                .IsRequired();

            // 配置字段约束
            builder.Property(w => w.Date)
                .IsRequired();

            builder.Property(w => w.TemperatureC)
                .IsRequired();

            builder.Property(w => w.Summary)
                .HasMaxLength(200)
                .IsRequired();

            // 配置索引
            builder.HasIndex(w => w.Date);
        }
    }
}
