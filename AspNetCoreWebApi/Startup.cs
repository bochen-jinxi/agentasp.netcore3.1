using AspNetCoreWebApi.Extensions;
using AspNetCoreWebApi.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspNetCoreWebApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // 配置Kestrel高性能参数
            services.AddHighPerformanceKestrel();

            // 注册应用程序服务
            services.AddApplicationServices(Configuration);

            // 配置JWT认证
            services.AddJwtAuthentication(Configuration);

            // 配置响应缓存
            services.AddCustomResponseCaching();

            // 配置分布式Redis缓存
            services.AddDistributedRedisCache(Configuration);

            // 配置消息队列
            services.AddMessageQueue(Configuration);

            // 配置CORS
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            // 配置控制器
            services.AddControllers(options =>
            {
                options.Filters.Add<GlobalExceptionFilter>();
                options.Filters.Add<ValidationFilter>();
            });

            // 配置Swagger
            services.AddSwaggerDocumentation();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ASP.NET Core WebAPI v1"));
            }

            app.UseHttpsRedirection();

            // 使用响应缓存中间件
            app.UseResponseCaching();

            app.UseRouting();

            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();

            // 使用自定义日志中间件
            app.UseCustomLogging();

            // 初始化消息队列（确保队列在启动时创建）
            app.UseMessageQueue();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
