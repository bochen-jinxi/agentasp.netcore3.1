using AspNetCoreWebApi.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;

namespace AspNetCoreWebApi.Filters
{
    /// <summary>
    /// 模型验证过滤器
    /// </summary>
    public class ValidationFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState
                    .Where(e => e.Value.Errors.Count > 0)
                    .SelectMany(e => e.Value.Errors.Select(x => x.ErrorMessage))
                    .ToList();

                var response = ApiResponse<object>.Fail(
                    "数据验证失败",
                    "VALIDATION_ERROR",
                    errors
                );

                context.Result = new BadRequestObjectResult(response);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // 不需要实现
        }
    }
}
