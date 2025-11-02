using BenchmarkDotNet.Running;

namespace Infrastructure.Hosting.Common.RabbitMQ.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<RabbitMQStoreBenchmarks>(args: args);
    }
}
