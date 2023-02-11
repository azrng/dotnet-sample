using Common.Core.Results;
using Microsoft.AspNetCore.Mvc;
using SettingConfig.Dto;

namespace SettingConfig;

/// <summary>
/// 配置控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SystemSettingController : ControllerBase
{
    private readonly IConfigSettingService _configSettingService;

    public SystemSettingController(IConfigSettingService configSettingService)
    {
        _configSettingService = configSettingService;
    }

    /// <summary>
    /// 获取分页列表
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpGet("page")]
    public Task<GetSettingPageListResult> GetPageListAsync(
        [FromQuery] GetSettingPageListRequest request)
    {
        return _configSettingService.GetPageListAsync(request);
    }

    /// <summary>
    /// 根据配置id查询启用版本的详情
    /// </summary>
    /// <param name="configId"></param>
    /// <returns></returns>
    [HttpGet("{configId:int}/enabled")]
    public Task<IResultModel<GetConfigDetailsResult>> GetEnabledVersionByConfigIdAsync(int configId)
    {
        return _configSettingService.GetEnabledVersionByConfigIdAsync(configId);
    }

    /// <summary>
    /// 更新配置启用版本详情
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPut("version")]
    public Task<IResultModel<bool>> UpdateConfigVersionDetailsAsync(
        [FromBody] UpdateConfigVersionDetailsRequest request)
    {
        return _configSettingService.UpdateConfigVersionDetailsAsync(request);
    }

    /// <summary>
    /// 根据配置key查询版本列表
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    [HttpGet("version/list/{key}")]
    public Task<List<GetConfigVersionListResult>> GetConfigVersionListAsync([FromRoute] string key)
    {
        return _configSettingService.GetConfigVersionListAsync(key);
    }

    /// <summary>
    /// 启用指定配置的版本
    /// </summary>
    /// <param name="key"></param>
    /// <param name="versionId"></param>
    /// <returns></returns>
    [HttpPut("config/{key}/enabled/{versionId}")]
    public Task<IResultModel<bool>> EnabledVersionAsync([FromRoute] string key, [FromRoute] int versionId)
    {
        return _configSettingService.EnabledVersionAsync(key, versionId);
    }

    /// <summary>
    /// 删除指定配置(逻辑删除)
    /// </summary>
    /// <param name="configId"></param>
    /// <returns></returns>
    [HttpDelete("{configId:int}")]
    public Task<IResultModel<bool>> DeleteConfigAsync([FromRoute] int configId)
    {
        return _configSettingService.DeleteConfigAsync(configId);
    }
}