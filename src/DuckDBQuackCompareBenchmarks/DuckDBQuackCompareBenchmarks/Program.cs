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
        GetConnectionString();

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
            typeof(ColdQueryBench),
            typeof(QueryBench),
            typeof(ResultSetBench),
            typeof(ReaderAccessBench),
            typeof(ConcurrencyBench),
            typeof(PoolBench),
            typeof(InsertPerRowBench),
            typeof(InsertBatchBench),
        }).Run(args, config);
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUACK_PROTOCOL_CONNECTION_STRING")
            ?? "Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true;Catalog=test";

        if (!HasExplicitCatalog(connectionString))
        {
            throw new InvalidOperationException(
                "Benchmark connection string must explicitly include Catalog=... or Database=... so all clients query the same remote catalog.");
        }

        return connectionString;
    }

    private static bool HasExplicitCatalog(string connectionString)
    {
        if (connectionString.StartsWith("quack:", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("jdbc:quack:", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString.Contains("catalog=", StringComparison.OrdinalIgnoreCase) ||
                   connectionString.Contains("database=", StringComparison.OrdinalIgnoreCase);
        }

        return connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static part =>
                part.StartsWith("Catalog=", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
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
