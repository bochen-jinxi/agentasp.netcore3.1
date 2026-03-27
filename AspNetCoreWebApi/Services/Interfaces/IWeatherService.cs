using System.Collections.Generic;
using System.Threading.Tasks;
using AspNetCoreWebApi.DTOs.Requests;
using AspNetCoreWebApi.DTOs.Responses;

namespace AspNetCoreWebApi.Services.Interfaces
{
    /// <summary>
    /// 天气预报服务接口
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>
        /// 获取所有天气预报
        /// </summary>
        Task<ApiResponse<IEnumerable<WeatherForecastResponse>>> GetAllAsync();

        /// <summary>
        /// 根据ID获取天气预报
        /// </summary>
        Task<ApiResponse<WeatherForecastResponse>> GetByIdAsync(string id);

        /// <summary>
        /// 创建天气预报
        /// </summary>
        Task<ApiResponse<WeatherForecastResponse>> CreateAsync(WeatherForecastRequest request);

        /// <summary>
        /// 更新天气预报
        /// </summary>
        Task<ApiResponse<WeatherForecastResponse>> UpdateAsync(string id, WeatherForecastRequest request);

        /// <summary>
        /// 删除天气预报
        /// </summary>
        Task<ApiResponse<bool>> DeleteAsync(string id);
    }
}
