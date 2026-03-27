using AspNetCoreWebApi.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreWebApi.Controllers
{
    /// <summary>
    /// 健康检查控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>
        /// 健康检查
        /// </summary>
        [HttpGet]
        public IActionResult Check()
        {
            return Ok(ApiResponse<string>.Ok("Healthy", "服务正常运行"));
        }

        /// <summary>
        /// Ping测试
        /// </summary>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(ApiResponse<string>.Ok("Pong", "Ping成功"));
        }
    }
}
