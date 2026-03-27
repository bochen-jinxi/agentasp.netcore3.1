using System;
using System.Threading.Tasks;
using AspNetCoreWebApi.Data;
using AspNetCoreWebApi.DTOs.Responses;
using AspNetCoreWebApi.Models.Entities;
using AspNetCoreWebApi.Services.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessagePack;

namespace AspNetCoreWebApi.Controllers
{
    /// <summary>
    /// 电商订单控制器 - 本地消息表 + 微批量发布（事务保证 + 实时性）
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IMicroBatchPublisher _microBatchPublisher;

        public OrderController(
            ApplicationDbContext dbContext,
            IMicroBatchPublisher microBatchPublisher)
        {
            _dbContext = dbContext;
            _microBatchPublisher = microBatchPublisher;
        }

        /// <summary>
        /// 创建订单（事务保证 - 订单和消息记录原子性保存）
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            // 开启本地事务
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 创建订单
                var order = new Order
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    Amount = request.Amount,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Orders.Add(order);

                // 2. 构建订单创建事件
                var orderCreatedEvent = new OrderCreatedEvent
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    ProductId = order.ProductId,
                    Quantity = order.Quantity,
                    Amount = order.Amount,
                    CreatedAt = order.CreatedAt
                };

                // 3. 序列化消息
                var messageBytes = MessagePackSerializer.Serialize(orderCreatedEvent, MessagePackSerializerOptions.Standard);
                var messageContent = Convert.ToBase64String(messageBytes);

                // 4. 写入消息记录（同一事务，确保原子性）
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    MessageType = "OrderCreated",
                    QueueName = "order_events",
                    Content = messageContent,
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.OutboxMessages.Add(outboxMessage);

                // 5. 提交事务（订单和消息记录一起持久化）
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                // 6. 返回成功（用户立即看到结果，后台服务异步发送消息）
                return Ok(ApiResponse<Order>.Ok(order, "订单创建成功，正在处理中"));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ApiResponse<string>.Fail($"订单创建失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 创建订单（微批量模式 - 事务保证 + 20ms窗口批量发送）
        /// </summary>
        [HttpPost("fast")]
        public async Task<IActionResult> CreateOrderFast([FromBody] CreateOrderRequest request)
        {
            // 开启本地事务
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 创建订单
                var order = new Order
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    Amount = request.Amount,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Orders.Add(order);

                // 2. 构建订单创建事件
                var orderCreatedEvent = new OrderCreatedEvent
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    ProductId = order.ProductId,
                    Quantity = order.Quantity,
                    Amount = order.Amount,
                    CreatedAt = order.CreatedAt
                };

                // 3. 序列化消息
                var messageBytes = MessagePackSerializer.Serialize(orderCreatedEvent, MessagePackSerializerOptions.Standard);
                var messageContent = Convert.ToBase64String(messageBytes);

                // 4. 写入消息记录（同一事务，确保原子性）
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    MessageType = "OrderCreated",
                    QueueName = "order_events",
                    Content = messageContent,
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.OutboxMessages.Add(outboxMessage);

                // 5. 提交事务（订单和消息记录一起持久化）
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                // 6. 微批量发布（非阻塞，20ms窗口内批量发送，提升性能）
                await _microBatchPublisher.PublishAsync("order_events", messageBytes);

                // 7. 返回成功
                return Ok(ApiResponse<Order>.Ok(order, "订单创建成功，正在处理中"));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ApiResponse<string>.Fail($"订单创建失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 创建订单（关键业务 - 事务保证 + 等待消息确认）
        /// </summary>
        [HttpPost("reliable")]
        public async Task<IActionResult> CreateOrderReliable([FromBody] CreateOrderRequest request)
        {
            // 开启本地事务
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 创建订单
                var order = new Order
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    ProductId = request.ProductId,
                    Quantity = request.Quantity,
                    Amount = request.Amount,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Orders.Add(order);

                // 2. 构建订单创建事件
                var orderCreatedEvent = new OrderCreatedEvent
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    ProductId = order.ProductId,
                    Quantity = order.Quantity,
                    Amount = order.Amount,
                    CreatedAt = order.CreatedAt
                };

                // 3. 序列化消息
                var messageBytes = MessagePackSerializer.Serialize(orderCreatedEvent, MessagePackSerializerOptions.Standard);
                var messageContent = Convert.ToBase64String(messageBytes);

                // 4. 写入消息记录（同一事务）
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    MessageType = "OrderCreated",
                    QueueName = "order_events",
                    Content = messageContent,
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.OutboxMessages.Add(outboxMessage);

                // 5. 提交事务
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                // 6. 立即发送消息并等待确认（确保消息送达）
                await _microBatchPublisher.PublishWithConfirmAsync("order_events", messageBytes);

                // 7. 更新消息状态为已发送
                outboxMessage.Status = OutboxMessageStatus.Sent;
                outboxMessage.SentAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                // 8. 返回成功（消息已确认送达）
                return Ok(ApiResponse<Order>.Ok(order, "订单创建成功，消息已确认送达"));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ApiResponse<string>.Fail($"订单创建失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 查询订单状态
        /// </summary>
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetOrder(string orderId)
        {
            var order = await _dbContext.Orders.FindAsync(orderId);
            if (order == null)
            {
                return NotFound(ApiResponse<string>.Fail("订单不存在"));
            }

            return Ok(ApiResponse<Order>.Ok(order, "查询成功"));
        }
    }

    #region 订单相关实体

    /// <summary>
    /// 订单实体
    /// </summary>
    public class Order
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>
    /// 订单状态
    /// </summary>
    public enum OrderStatus
    {
        Pending = 0,      // 待支付
        Paid = 1,         // 已支付
        Processing = 2,   // 处理中
        Completed = 3,    // 已完成
        Cancelled = 4     // 已取消
    }

    /// <summary>
    /// 创建订单请求
    /// </summary>
    public class CreateOrderRequest
    {
        public string UserId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// 订单创建事件（用于消息传递）
    /// </summary>
    [MessagePackObject]
    public class OrderCreatedEvent
    {
        [Key(0)]
        public string OrderId { get; set; }
        [Key(1)]
        public string UserId { get; set; }
        [Key(2)]
        public string ProductId { get; set; }
        [Key(3)]
        public int Quantity { get; set; }
        [Key(4)]
        public decimal Amount { get; set; }
        [Key(5)]
        public DateTime CreatedAt { get; set; }
    }

    #endregion
}
