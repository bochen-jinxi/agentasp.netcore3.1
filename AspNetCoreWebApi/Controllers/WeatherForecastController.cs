using System.Threading.Tasks;
using AspNetCoreWebApi.DTOs.Requests;
using AspNetCoreWebApi.DTOs.Responses;
using AspNetCoreWebApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreWebApi.Controllers
{
    /// <summary>
    /// 天气预报API控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IWeatherService _weatherService;

        public WeatherForecastController(IWeatherService weatherService)
        {
            _weatherService = weatherService;
        }

        /// <summary>
        /// 获取所有天气预报
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _weatherService.GetAllAsync();
            return Ok(result);
        }

        /// <summary>
        /// 根据ID获取天气预报
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _weatherService.GetByIdAsync(id);
            if (!result.Success)
            {
                return NotFound(result);
            }
            return Ok(result);
        }

        /// <summary>
        /// 创建天气预报
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] WeatherForecastRequest request)
        {
            var result = await _weatherService.CreateAsync(request);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return CreatedAtAction(nameof(GetById), new { id = result.Data.Id }, result);
        }

        /// <summary>
        /// 更新天气预报
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] WeatherForecastRequest request)
        {
            var result = await _weatherService.UpdateAsync(id, request);
            if (!result.Success)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(result);
                }
                return BadRequest(result);
            }
            return Ok(result);
        }

        /// <summary>
        /// 删除天气预报
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _weatherService.DeleteAsync(id);
            if (!result.Success)
            {
                if (result.ErrorCode == "NOT_FOUND")
                {
                    return NotFound(result);
                }
                return BadRequest(result);
            }
            return NoContent();
        }
    }
}
