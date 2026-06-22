using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using Azrng.DuckDB.Data.Quack;

namespace DuckDBQuackCompareBenchmarks;

internal static class Program
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("QUACK_PROTOCOL_CONNECTION_STRING")
        ?? "Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true";

    private static readonly QuackConnectionConfig LocalConfig = QuackConnectionStringParser.Parse(ConnectionString);

    public static readonly string LocalCatalog = LocalConfig.Catalog;

    public static readonly string LocalAttachConnectionString = BuildLocalConnectionString(attach: true);

    public static readonly string LocalQueryConnectionString = BuildLocalConnectionString(attach: false);

    private static void Main(string[] args)
    {
        Console.WriteLine($"Benchmark target: {ConnectionString}");
        Console.WriteLine("Local ATTACH mode: Attach=true, queries reference the attached remote catalog.");
        Console.WriteLine("Local query mode: Attach=false, executed through quack_query.");
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

    private static string BuildLocalConnectionString(bool attach)
    {
        var parts = new List<string>
        {
            $"Host={LocalConfig.Host}",
            $"Port={LocalConfig.Port}",
            $"Token={LocalConfig.Token}",
            $"Catalog={LocalConfig.Catalog}",
            $"DisableSsl={LocalConfig.DisableSsl.ToString().ToLowerInvariant()}",
            $"Attach={attach.ToString().ToLowerInvariant()}",
        };

        if (!string.IsNullOrWhiteSpace(LocalConfig.ExtensionDirectory))
            parts.Add($"ExtensionDirectory={LocalConfig.ExtensionDirectory}");

        return string.Join(';', parts);
    }
}
