using Common.Core.Extension;

namespace SettingConfig.Dto;

/// <summary>
/// 获取配置版本列表返回类
/// </summary>
public class GetConfigVersionListResult
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
    /// 配置的值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 配置说明
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 配置版本
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public string CreateTimeStr => CreateTime.ToStandardString();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public string UpdateTimeStr => UpdateTime.ToStandardString();

    /// <summary>
    /// 是否禁用
    /// </summary>
    public bool IsDisabled { get; set; }
}