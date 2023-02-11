namespace SettingConfig.Dto;

/// <summary>
/// 更新配置版本详情请求类
/// </summary>
public class UpdateConfigVersionDetailsRequest
{
    /// <summary>
    /// 对应启用版本的标识
    /// </summary>
    public int VersionId { get; set; }

    /// <summary>
    /// 配置说明
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 配置的值
    /// </summary>
    public string Value { get; set; }
}