using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Azrng.DuckDB.Quack.Tests;

public class Startup
{
    public void ConfigureHost(IHostBuilder hostBuilder) { }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = Environment.GetEnvironmentVariable("QUACK_PROTOCOL_CONNECTION_STRING")
            ?? "Host=localhost;Port=9494;Token=E7231CE2CE78902BA280F3B9158BEB30;DisableSsl=true;Catalog=view";

        services.AddSingleton(new TestOptions { ConnectionString = connectionString });
        services.AddLogging(builder => builder.AddXUnit());
    }
}
