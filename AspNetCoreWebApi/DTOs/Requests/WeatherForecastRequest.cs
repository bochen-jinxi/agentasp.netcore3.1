using System;
using System.ComponentModel.DataAnnotations;

namespace AspNetCoreWebApi.DTOs.Requests
{
    /// <summary>
    /// 天气预报请求DTO
    /// </summary>
    public class WeatherForecastRequest
    {
        /// <summary>
        /// 预报日期
        /// </summary>
        [Required(ErrorMessage = "预报日期不能为空")]
        public DateTime Date { get; set; }

        /// <summary>
        /// 摄氏温度
        /// </summary>
        [Required(ErrorMessage = "摄氏温度不能为空")]
        [Range(-100, 100, ErrorMessage = "摄氏温度必须在-100到100之间")]
        public int TemperatureC { get; set; }

        /// <summary>
        /// 天气摘要
        /// </summary>
        [Required(ErrorMessage = "天气摘要不能为空")]
        [MaxLength(200, ErrorMessage = "天气摘要长度不能超过200个字符")]
        public string Summary { get; set; }
    }
}
