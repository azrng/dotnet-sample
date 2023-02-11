using System.Data;
using Common.Core.Extension;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SettingConfig.Repository;

namespace SettingConfig;

public static class ServiceCollectionExtensions
{
    internal static SettingUIOptions injectUIOptions;

    /// <summary>
    /// 添加配置服务
    /// </summary>
    /// <param name="services"></param>
    /// <param name="setupAction"></param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <returns></returns>
    public static IServiceCollection AddSettingConfig(this IServiceCollection services,
        Action<SettingUIOptions> setupAction)
    {
        services.AddControllers().AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        var setting = new SettingUIOptions();
        setupAction.Invoke(setting);

        // 校验逻辑
        if (setting.DbConnection.IsNullOrWhiteSpace())
            throw new ArgumentNullException("数据库地址参数不能为空");
        if (setting.DbSchema.IsNullOrWhiteSpace())
            throw new ArgumentNullException("数据库schema地址不能为空");

        injectUIOptions = setting;

        services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(setting.DbConnection));
        services.AddScoped<IDapperRepository, DapperRepository>();
        services.AddScoped<IDataSourceProvider, PgsqlDataSourceProvider>();
        
        services.AddScoped<IConfigSettingService, ConfigSettingService>();

        // 注入内存缓存 方便使用redis替换  
        services.AddDistributedMemoryCache();
        return services;
    }
}