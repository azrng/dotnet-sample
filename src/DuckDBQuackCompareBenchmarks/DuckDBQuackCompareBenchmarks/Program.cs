using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace DuckDBQuackCompareBenchmarks;

internal static class Program
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("QUACK_PROTOCOL_CONNECTION_STRING")
        ?? "Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true";

    private static void Main(string[] args)
    {
        Console.WriteLine($"Benchmark target: {ConnectionString}");
        SmokeChecks.RunAsync().GetAwaiter().GetResult();
        if (args.Any(static arg => string.Equals(arg, "--smoke-only", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Smoke checks passed.");
            return;
        }

        var config = ManualConfig.CreateMinimumViable()
            .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(JsonExporter.Default)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Method, MethodOrderPolicy.Alphabetical))
            .WithCultureInfo(System.Globalization.CultureInfo.InvariantCulture);

        BenchmarkSwitcher.FromTypes(new[]
        {
            typeof(ConnectionBench),
            typeof(QueryBench),
            typeof(ResultSetBench),
            typeof(ReaderAccessBench),
            typeof(ConcurrencyBench),
            typeof(PoolBench),
            typeof(InsertBench),
        }).Run(args, config);
    }
}
