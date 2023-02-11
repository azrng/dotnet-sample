using Common.Core.Results;
using SettingConfig.Dto;

namespace SettingConfig;

/// <summary>
/// 获取配置服务
/// </summary>
public interface IConfigSettingService
{
    /// <summary>
    /// 获取分页列表
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    Task<GetSettingPageListResult> GetPageListAsync(GetSettingPageListRequest request);

    /// <summary>
    /// 根据配置id查询启用版本的详情
    /// </summary>
    /// <param name="configId"></param>
    /// <returns></returns>
    Task<IResultModel<GetConfigDetailsResult>> GetEnabledVersionByConfigIdAsync(int configId);

    /// <summary>
    /// 更新配置启用版本详情
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    Task<IResultModel<bool>> UpdateConfigVersionDetailsAsync(UpdateConfigVersionDetailsRequest request);

    /// <summary>
    /// 查询配置版本列表
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    Task<List<GetConfigVersionListResult>> GetConfigVersionListAsync(string key);

    /// <summary>
    /// 启用指定配置的指定版本
    /// </summary>
    /// <param name="key"></param>
    /// <param name="versionId"></param>
    /// <returns></returns>
    Task<IResultModel<bool>> EnabledVersionAsync(string key, int versionId);

    /// <summary>
    /// 删除指定配置(逻辑删除)
    /// </summary>
    /// <param name="configId"></param>
    /// <returns></returns>
    Task<IResultModel<bool>> DeleteConfigAsync(int configId);

    /// <summary>
    /// 根据key查询配置
    /// </summary>
    /// <param name="configKey"></param>
    /// <param name="throwError"></param>
    /// <returns></returns>
    Task<string> GetConfigContentAsync(string configKey, bool throwError = true);

    /// <summary>
    /// 根据key查询配置
    /// </summary>
    /// <param name="crConfigKey"></param>
    /// <param name="throwError"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T> GetConfigAsync<T>(string crConfigKey, bool throwError = true);

    // 增加查询的接口  并保存缓存
}