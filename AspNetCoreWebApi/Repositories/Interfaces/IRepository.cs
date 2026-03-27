using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AspNetCoreWebApi.Models.Entities;

namespace AspNetCoreWebApi.Repositories.Interfaces
{
    /// <summary>
    /// 通用仓储接口
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    public interface IRepository<T> where T : BaseEntity
    {
        /// <summary>
        /// 根据ID获取实体
        /// </summary>
        Task<T> GetByIdAsync(string id);

        /// <summary>
        /// 获取所有实体
        /// </summary>
        Task<IEnumerable<T>> GetAllAsync();

        /// <summary>
        /// 根据条件查找实体
        /// </summary>
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 添加实体
        /// </summary>
        Task<T> AddAsync(T entity);

        /// <summary>
        /// 更新实体
        /// </summary>
        Task UpdateAsync(T entity);

        /// <summary>
        /// 删除实体
        /// </summary>
        Task DeleteAsync(string id);

        /// <summary>
        /// 检查实体是否存在
        /// </summary>
        Task<bool> ExistsAsync(string id);
    }
}
