using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Middleware
{
    /// <summary>
    /// 请求日志中间件
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("请求开始: {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            try
            {
                await _next(context);
            }
            finally
            {
                var elapsed = DateTime.UtcNow - startTime;

                _logger.LogInformation("请求结束: {Method} {Path} - 状态码: {StatusCode} - 耗时: {Elapsed}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds);
            }
        }
    }
}
