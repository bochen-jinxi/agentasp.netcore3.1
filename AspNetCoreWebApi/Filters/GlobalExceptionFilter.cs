using AspNetCoreWebApi.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Filters
{
    /// <summary>
    /// 全局异常过滤器
    /// </summary>
    public class GlobalExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<GlobalExceptionFilter> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public void OnException(ExceptionContext context)
        {
            _logger.LogError(context.Exception, "发生未处理的异常: {Message}", context.Exception.Message);

            var response = ApiResponse<object>.Fail(
                _environment.IsDevelopment() ? context.Exception.Message : "服务器内部错误",
                "INTERNAL_ERROR"
            );

            context.Result = new ObjectResult(response)
            {
                StatusCode = 500
            };

            context.ExceptionHandled = true;
        }
    }
}
