using System;

namespace AspNetCoreWebApi.Models.Entities
{
    /// <summary>
    /// 天气预报实体类
    /// </summary>
    public class WeatherForecast : BaseEntity
    {
        /// <summary>
        /// 预报日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 摄氏温度
        /// </summary>
        public int TemperatureC { get; set; }

        /// <summary>
        /// 华氏温度（计算属性）
        /// </summary>
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        /// <summary>
        /// 天气摘要
        /// </summary>
        public string Summary { get; set; }
    }
}
