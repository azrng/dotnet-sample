namespace SettingConfig.Dto;

/// <summary>
/// 获取配置分页列表请求类
/// </summary>
public class GetSettingPageListRequest
{
    /// <summary>
    /// 页码
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// 页数
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// 关键字
    /// </summary>
    public string Keyword { get; set; }

    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; }
}