using System.Reflection;

namespace SettingConfig;

/// <summary>
/// 配置UI选项设置
/// </summary>
public class SettingUIOptions
{
    /// <summary>
    /// 配置中心页面路由
    /// </summary>
    public string RoutePrefix { get; set; } = "systemsetting";

    /// <summary>
    /// 环境变量库连接字符串
    /// </summary>
    public string DbConnection { get; set; }

    /// <summary>
    /// 数据库模式
    /// </summary>
    public string DbSchema { get; set; } = "setting";

    /// <summary>
    /// 获取或设置用于检索setting-ui页面的Stream函数
    /// </summary>
    internal Func<Stream> IndexStream { get; } = () =>
        typeof(SettingUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("SettingConfig.wwwroot.index.html");

    /// <summary>页面Title</summary>
    public string PageTitle { get; set; } = "系统配置页面";

    /// <summary>
    /// 页面说明
    /// </summary>
    public string PageDescription { get; set; } = "系统设置界面";
}