namespace SettingConfig.Dto;

/// <summary>
/// 获取配置信息
/// </summary>
public class GetConfigInfoDto
{
    /// <summary>
    /// 配置key(唯一)
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; }
}