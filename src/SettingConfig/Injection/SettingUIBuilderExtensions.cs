using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SettingConfig;

/// <summary>
/// 配置UI构建扩展
/// </summary>
public static class SettingUIBuilderExtensions
{
    /// <summary>
    /// 注册配置UI中间件
    /// </summary>
    public static IApplicationBuilder UseSettingUI(this IApplicationBuilder app)
    {
        using (var scope = app.ApplicationServices.CreateScope())
        {
            var dataSourceProvider = scope.ServiceProvider.GetRequiredService<IDataSourceProvider>();
            dataSourceProvider.InitAsync().GetAwaiter().GetResult();
        }

        return app.UseMiddleware<SettingUIMiddleware>();
    }
}