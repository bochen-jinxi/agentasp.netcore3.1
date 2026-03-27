using System.Collections.Generic;

namespace AspNetCoreWebApi.DTOs.Responses
{
    /// <summary>
    /// 统一API响应模型
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应数据
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 错误代码
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// 验证错误列表
        /// </summary>
        public IEnumerable<string> Errors { get; set; }

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static ApiResponse<T> Ok(T data, string message = "操作成功")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        public static ApiResponse<T> Fail(string message, string errorCode = null, IEnumerable<string> errors = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = errors
            };
        }
    }
}
