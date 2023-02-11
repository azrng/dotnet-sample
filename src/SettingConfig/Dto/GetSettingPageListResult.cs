namespace SettingConfig.Dto;

/// <summary>
/// 获取配置分页列表
/// </summary>
public class GetSettingPageListResult
{
    public GetSettingPageListResult(int total = 0, IEnumerable<GetSettingInfoDto> rows = null)
    {
        Total = total;
        Rows = rows ?? Enumerable.Empty<GetSettingInfoDto>();
    }

    /// <summary>
    /// 总条数
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// 配置列表
    /// </summary>
    public IEnumerable<GetSettingInfoDto> Rows { get; }
}

/// <summary>
/// 获取配置信息
/// </summary>
public class GetSettingInfoDto
{
    /// <summary>
    /// 标识ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 配置key(唯一)
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; }


    /// <summary>
    /// 配置的值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 配置说明
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// 对应启用版本的标识
    /// </summary>
    public int VersionId { get; set; }
}