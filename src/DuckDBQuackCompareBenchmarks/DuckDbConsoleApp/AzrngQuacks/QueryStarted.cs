using System.Diagnostics;
using Azrng.ConsoleApp.DependencyInjection;
using Azrng.DuckDB.Quack;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DuckDbConsoleApp.AzrngQuacks;

public class QueryStarted : IServiceStart
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<QueryStarted> _logger;

    public QueryStarted(IConfiguration configuration, ILogger<QueryStarted> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var connection = _configuration["DuckDb:duckflight"];
        _logger.LogInformation($"连接信息：{connection}");
        var quackConnect = new QuackConnection(connection);
        var sql = "select count(*) from duckflight.source.orders";

        await quackConnect.OpenAsync();

        var stopwatch = Stopwatch.StartNew();
        var result = await quackConnect.ExecuteScalarAsync(sql);
        var end = stopwatch.ElapsedMilliseconds;
        Console.WriteLine($"结果：{result}  耗时：{end}毫秒");
    }

    public string Title => "query started";
}