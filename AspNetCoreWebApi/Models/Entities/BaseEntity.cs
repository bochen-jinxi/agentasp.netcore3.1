using System;

namespace AspNetCoreWebApi.Models.Entities
{
    /// <summary>
    /// 实体基类
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// 实体唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
}
