import pika
import time
import sys

def get_queue_stats():
    try:
        connection = pika.BlockingConnection(
            pika.URLParameters('amqp://admin:123456@192.168.1.23:32558/')
        )
        channel = connection.channel()
        
        result = channel.queue_declare(queue='async_tasks', durable=True, passive=True)
        message_count = result.method.message_count
        consumer_count = result.method.consumer_count
        
        connection.close()
        return message_count, consumer_count
    except Exception as e:
        return None, None

if __name__ == "__main__":
    print("=" * 50)
    print("RabbitMQ 队列监控")
    print("队列: async_tasks")
    print("=" * 50)
    
    # 获取一次统计
    message_count, consumer_count = get_queue_stats()
    if message_count is not None:
        print(f"消息数: {message_count:,}")
        print(f"消费者数: {consumer_count}")
    else:
        print("无法连接到 RabbitMQ")
    print("=" * 50)
