namespace SettingConfig.Dto;

/// <summary>
/// 查询指定配置配置启用的版本详情
/// </summary>
public class GetConfigDetailsResult
{
    /// <summary>
    /// 对应启用版本的标识
    /// </summary>
    public int VersionId { get; set; }
    
    /// <summary>
    /// 配置key(唯一)
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 配置说明
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 配置的值
    /// </summary>
    public string Value { get; set; }
}