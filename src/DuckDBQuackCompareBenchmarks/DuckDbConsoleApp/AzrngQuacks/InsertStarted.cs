using Azrng.ConsoleApp.DependencyInjection;
using Azrng.DuckDB.Quack;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DuckDbConsoleApp.AzrngQuacks;

public class InsertStarted : IServiceStart
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<QueryStarted> _logger;

    public InsertStarted(IConfiguration configuration, ILogger<QueryStarted> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var connection = _configuration["DuckDb:duckflight"];
        _logger.LogInformation($"连接信息：{connection}");
        var quackConnect = new QuackConnection(connection);
        var sql = @"insert into source.orders
  select
      i::BIGINT as order_id,
      cast(floor(random() * 1000000) + 1 as BIGINT) as user_id,
      ['created', 'paid', 'shipped', 'completed', 'cancelled'][cast(floor(random() * 5) + 1 as INTEGER)] as order_status,
      cast(round(random() * 10000, 2) as DECIMAL(10, 2)) as order_amount,
      ['wechat', 'alipay', 'card', 'cash'][cast(floor(random() * 4) + 1 as INTEGER)] as payment_method,
      created_at,
      created_at + cast(floor(random() * 30) as INTEGER) * interval '1 day' as updated_at
  from (
      select
          generate_series as i,
          timestamp '2026-01-01'
              + cast(floor(random() * 365) as INTEGER) * interval '1 day'
              + cast(floor(random() * 86400) as INTEGER) * interval '1 second' as created_at
      from generate_series(1, 10000000)
  ) t;";

        await quackConnect.OpenAsync();
        var result = await quackConnect.ExecuteAsync(sql);
        Console.WriteLine("结果：" + result);
    }

    public string Title => "添加";
}