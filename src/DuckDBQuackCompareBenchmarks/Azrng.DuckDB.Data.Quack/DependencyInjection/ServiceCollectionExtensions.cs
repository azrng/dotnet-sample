using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azrng.DuckDB.Data.Quack.DependencyInjection;

/// <summary>
/// Azrng.DuckDB.Data.Quack 依赖注入扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 QuackDataProvider 为单例
    /// </summary>
    public static IServiceCollection AddQuackDataProvider(this IServiceCollection services, QuackConnectionConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(sp =>
        {
            var provider = new QuackDataProvider(config);
            if (config.Attach)
                provider.AttachRemote();
            return provider;
        });

        return services;
    }

    /// <summary>
    /// 注册 QuackDataProvider 为单例（从 IConfiguration 读取配置）
    /// 配置节路径: "Quack" -> Host, Port, Token, Catalog, Attach, DisableSsl
    /// </summary>
    public static IServiceCollection AddQuackDataProvider(this IServiceCollection services, IConfiguration configuration, string sectionName = "Quack")
    {
        var config = new QuackConnectionConfig();
        configuration.GetSection(sectionName).Bind(config);
        return services.AddQuackDataProvider(config);
    }

    /// <summary>
    /// 注册 QuackDataProvider 为单例（从连接字符串解析）
    /// </summary>
    public static IServiceCollection AddQuackDataProvider(this IServiceCollection services, string connectionString)
    {
        var config = QuackConnectionStringParser.Parse(connectionString);
        return services.AddQuackDataProvider(config);
    }
}
