using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AspNetCoreWebApi.DTOs.Requests;
using AspNetCoreWebApi.DTOs.Responses;
using AspNetCoreWebApi.Models.Entities;
using AspNetCoreWebApi.Repositories.Interfaces;
using AspNetCoreWebApi.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace AspNetCoreWebApi.Services.Implementations
{
    /// <summary>
    /// 天气预报服务实现类
    /// </summary>
    public class WeatherService : IWeatherService
    {
        private readonly IRepository<WeatherForecast> _repository;
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(IRepository<WeatherForecast> repository, ILogger<WeatherService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<ApiResponse<IEnumerable<WeatherForecastResponse>>> GetAllAsync()
        {
            try
            {
                var entities = await _repository.GetAllAsync();
                var responses = new List<WeatherForecastResponse>();

                foreach (var entity in entities)
                {
                    responses.Add(MapToResponse(entity));
                }

                return ApiResponse<IEnumerable<WeatherForecastResponse>>.Ok(responses, "获取天气预报列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取天气预报列表失败");
                return ApiResponse<IEnumerable<WeatherForecastResponse>>.Fail("获取天气预报列表失败", "GET_ALL_ERROR");
            }
        }

        public async Task<ApiResponse<WeatherForecastResponse>> GetByIdAsync(string id)
        {
            try
            {
                var entity = await _repository.GetByIdAsync(id);
                if (entity == null)
                {
                    return ApiResponse<WeatherForecastResponse>.Fail("未找到指定的天气预报", "NOT_FOUND");
                }

                var response = MapToResponse(entity);
                return ApiResponse<WeatherForecastResponse>.Ok(response, "获取天气预报成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取天气预报失败，ID: {Id}", id);
                return ApiResponse<WeatherForecastResponse>.Fail("获取天气预报失败", "GET_ERROR");
            }
        }

        public async Task<ApiResponse<WeatherForecastResponse>> CreateAsync(WeatherForecastRequest request)
        {
            try
            {
                var entity = new WeatherForecast
                {
                    Date = request.Date,
                    TemperatureC = request.TemperatureC,
                    Summary = request.Summary
                };

                var createdEntity = await _repository.AddAsync(entity);
                var response = MapToResponse(createdEntity);

                _logger.LogInformation("创建天气预报成功，ID: {Id}", createdEntity.Id);
                return ApiResponse<WeatherForecastResponse>.Ok(response, "创建天气预报成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建天气预报失败");
                return ApiResponse<WeatherForecastResponse>.Fail("创建天气预报失败", "CREATE_ERROR");
            }
        }

        public async Task<ApiResponse<WeatherForecastResponse>> UpdateAsync(string id, WeatherForecastRequest request)
        {
            try
            {
                var exists = await _repository.ExistsAsync(id);
                if (!exists)
                {
                    return ApiResponse<WeatherForecastResponse>.Fail("未找到指定的天气预报", "NOT_FOUND");
                }

                var entity = await _repository.GetByIdAsync(id);
                entity.Date = request.Date;
                entity.TemperatureC = request.TemperatureC;
                entity.Summary = request.Summary;

                await _repository.UpdateAsync(entity);
                var response = MapToResponse(entity);

                _logger.LogInformation("更新天气预报成功，ID: {Id}", id);
                return ApiResponse<WeatherForecastResponse>.Ok(response, "更新天气预报成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新天气预报失败，ID: {Id}", id);
                return ApiResponse<WeatherForecastResponse>.Fail("更新天气预报失败", "UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string id)
        {
            try
            {
                var exists = await _repository.ExistsAsync(id);
                if (!exists)
                {
                    return ApiResponse<bool>.Fail("未找到指定的天气预报", "NOT_FOUND");
                }

                await _repository.DeleteAsync(id);
                _logger.LogInformation("删除天气预报成功，ID: {Id}", id);

                return ApiResponse<bool>.Ok(true, "删除天气预报成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除天气预报失败，ID: {Id}", id);
                return ApiResponse<bool>.Fail("删除天气预报失败", "DELETE_ERROR");
            }
        }

        /// <summary>
        /// 将实体映射为响应DTO
        /// </summary>
        private WeatherForecastResponse MapToResponse(WeatherForecast entity)
        {
            return new WeatherForecastResponse
            {
                Id = entity.Id,
                Date = entity.Date,
                TemperatureC = entity.TemperatureC,
                TemperatureF = entity.TemperatureF,
                Summary = entity.Summary
            };
        }
    }
}
