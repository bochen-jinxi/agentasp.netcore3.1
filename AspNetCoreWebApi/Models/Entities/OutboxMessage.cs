using System;

namespace AspNetCoreWebApi.Models.Entities
{
    /// <summary>
    /// 发件箱消息（本地消息表模式 - 确保消息可靠投递）
    /// </summary>
    public class OutboxMessage
    {
        /// <summary>
        /// 消息唯一标识
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 消息类型（如：OrderCreated, PaymentCompleted）
        /// </summary>
        public string MessageType { get; set; }

        /// <summary>
        /// 目标队列名称
        /// </summary>
        public string QueueName { get; set; }

        /// <summary>
        /// 消息内容（JSON 或 MessagePack 二进制 Base64）
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 消息状态
        /// </summary>
        public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>
        /// 下次重试时间
        /// </summary>
        public DateTime? NextRetryAt { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 发送时间
        /// </summary>
        public DateTime? SentAt { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 消息状态
    /// </summary>
    public enum OutboxMessageStatus
    {
        /// <summary>
        /// 待发送
        /// </summary>
        Pending = 0,

        /// <summary>
        /// 已发送
        /// </summary>
        Sent = 1,

        /// <summary>
        /// 发送失败
        /// </summary>
        Failed = 2,

        /// <summary>
        /// 已取消（超过最大重试次数）
        /// </summary>
        Cancelled = 3
    }
}
