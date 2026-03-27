using AspNetCoreWebApi.Middleware;
using Microsoft.AspNetCore.Builder;

namespace AspNetCoreWebApi.Extensions
{
    /// <summary>
    /// 应用构建器扩展类
    /// </summary>
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// 使用自定义日志中间件
        /// </summary>
        public static IApplicationBuilder UseCustomLogging(this IApplicationBuilder app)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
            return app;
        }
    }
}
