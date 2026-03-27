import asyncio
import aiohttp
import time
import statistics
from datetime import datetime

# 测试配置
TARGET_URL = "http://localhost:5000/api/v1/HighPerformance/ping"
TOTAL_REQUESTS = 100000000  # 1亿个请求
TEST_DURATION = 60  # 60秒
CONCURRENT_CONNECTIONS = 2000  # 并发连接数（优化后增加）

# 统计数据
class Stats:
    def __init__(self):
        self.success_count = 0
        self.fail_count = 0
        self.response_times = []
        self.start_time = None
        self.end_time = None

stats = Stats()

async def send_request(session, request_id):
    """发送单个GET请求"""
    start = time.time()
    try:
        async with session.get(TARGET_URL) as response:
            await response.text()
            elapsed = time.time() - start
            stats.response_times.append(elapsed)
            if response.status == 200:
                stats.success_count += 1
            else:
                stats.fail_count += 1
    except Exception as e:
        stats.fail_count += 1
        elapsed = time.time() - start
        stats.response_times.append(elapsed)

async def run_load_test():
    """运行负载测试"""
    print(f"开始压力测试...")
    print(f"目标URL: {TARGET_URL}")
    print(f"测试时长: {TEST_DURATION} 秒")
    print(f"并发连接数: {CONCURRENT_CONNECTIONS}")
    print(f"目标请求数: {TOTAL_REQUESTS:,}")
    print("-" * 50)

    stats.start_time = time.time()
    end_time = stats.start_time + TEST_DURATION

    connector = aiohttp.TCPConnector(limit=CONCURRENT_CONNECTIONS, limit_per_host=CONCURRENT_CONNECTIONS)
    timeout = aiohttp.ClientTimeout(total=5)

    async with aiohttp.ClientSession(connector=connector, timeout=timeout) as session:
        request_id = 0
        tasks = []

        while time.time() < end_time and request_id < TOTAL_REQUESTS:
            # 创建一批并发任务
            batch_size = min(CONCURRENT_CONNECTIONS, TOTAL_REQUESTS - request_id)
            for _ in range(batch_size):
                if time.time() >= end_time:
                    break
                task = asyncio.create_task(send_request(session, request_id))
                tasks.append(task)
                request_id += 1

            # 等待这批任务完成
            if tasks:
                await asyncio.gather(*tasks, return_exceptions=True)
                tasks = []

            # 每秒输出一次进度
            elapsed = time.time() - stats.start_time
            if elapsed >= 1:
                current_rps = (stats.success_count + stats.fail_count) / elapsed
                print(f"已运行: {elapsed:.1f}s | 请求数: {stats.success_count + stats.fail_count:,} | RPS: {current_rps:.0f} | 成功率: {stats.success_count/(stats.success_count+stats.fail_count)*100:.1f}%")

    stats.end_time = time.time()

def print_results():
    """打印测试结果"""
    total_time = stats.end_time - stats.start_time
    total_requests = stats.success_count + stats.fail_count
    actual_rps = total_requests / total_time if total_time > 0 else 0

    print("\n" + "=" * 50)
    print("压力测试结果")
    print("=" * 50)
    print(f"测试时长: {total_time:.2f} 秒")
    print(f"总请求数: {total_requests:,}")
    print(f"成功请求: {stats.success_count:,}")
    print(f"失败请求: {stats.fail_count:,}")
    print(f"成功率: {stats.success_count/total_requests*100:.2f}%" if total_requests > 0 else "成功率: 0%")
    print(f"实际RPS: {actual_rps:.2f} 请求/秒")

    if stats.response_times:
        print(f"\n响应时间统计:")
        print(f"  平均: {statistics.mean(stats.response_times)*1000:.2f} ms")
        print(f"  中位数: {statistics.median(stats.response_times)*1000:.2f} ms")
        if len(stats.response_times) > 1:
            print(f"  标准差: {statistics.stdev(stats.response_times)*1000:.2f} ms")
        print(f"  最小: {min(stats.response_times)*1000:.2f} ms")
        print(f"  最大: {max(stats.response_times)*1000:.2f} ms")

    print("\n" + "=" * 50)
    print(f"要达到1亿请求/分钟，需要RPS: {100000000/60:,.0f}")
    print(f"当前测试RPS: {actual_rps:,.0f}")
    print(f"差距: {100000000/60/actual_rps:.1f}x" if actual_rps > 0 else "差距: N/A")
    print("=" * 50)

if __name__ == "__main__":
    asyncio.run(run_load_test())
    print_results()
