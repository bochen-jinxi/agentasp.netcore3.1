using System;

namespace AspNetCoreWebApi.DTOs.Responses
{
    /// <summary>
    /// 天气预报响应DTO
    /// </summary>
    public class WeatherForecastResponse
    {
        /// <summary>
        /// 实体ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 预报日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 摄氏温度
        /// </summary>
        public int TemperatureC { get; set; }

        /// <summary>
        /// 华氏温度
        /// </summary>
        public int TemperatureF { get; set; }

        /// <summary>
        /// 天气摘要
        /// </summary>
        public string Summary { get; set; }
    }
}
