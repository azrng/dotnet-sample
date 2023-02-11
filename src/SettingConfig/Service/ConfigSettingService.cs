using Common.Core.Exceptions;
using Common.Core.Extension;
using Common.Core.Results;
using Common.Core.Service;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SettingConfig.Dto;

namespace SettingConfig;

public class ConfigSettingService : BaseService, IConfigSettingService
{
    private readonly IDataSourceProvider _dataSourceProvider;
    private readonly ILogger<ConfigSettingService> _logger;
    private readonly IDistributedCache _cache;

    public ConfigSettingService(IDataSourceProvider dataSourceProvider,
        ILogger<ConfigSettingService> logger,
        IDistributedCache cache)
    {
        _dataSourceProvider = dataSourceProvider;
        _logger = logger;
        _cache = cache;
    }

    public async Task<GetSettingPageListResult> GetPageListAsync(GetSettingPageListRequest request)
    {
        var total = await _dataSourceProvider.GetConfigCount();
        var row = await _dataSourceProvider.GetPageListAsync(request.PageIndex, request.PageSize, request.Keyword,
            request.Version);

        return new GetSettingPageListResult(total, row);
    }

    public async Task<IResultModel<GetConfigDetailsResult>> GetEnabledVersionByConfigIdAsync(int configId)
    {
        var config = await _dataSourceProvider.GetConfigDetails(configId);
        if (config is not null) return Success(config);
        _logger.LogError($"获取启用的版本 配置标识无效：{configId}");
        return Error<GetConfigDetailsResult>("配置标识无效");
    }

    public async Task<IResultModel<bool>> UpdateConfigVersionDetailsAsync(UpdateConfigVersionDetailsRequest request)
    {
        try
        {
            var key = await _dataSourceProvider.GetConfigKeyAsync(request.VersionId);
            if (key.IsNullOrWhiteSpace())
                return Error<bool>("配置版本标识无效");

            // 清除缓存
            var cacheKey = SettingConfigConst.ConfigPrefix + key;
            await _cache.RemoveAsync(cacheKey);
            var flag = await _dataSourceProvider.UpdateConfigVersionAsync(request.VersionId, request.Value,
                request.Description, string.Empty);
            return flag ? Success(true) : Error<bool>("更新失败");
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"更新版本内容报错  message：{e.Message}");
            return Error<bool>("更新失败");
        }
    }

    public Task<List<GetConfigVersionListResult>> GetConfigVersionListAsync(string key)
    {
        return _dataSourceProvider.GetConfigVersionListAsync(key);
    }

    public async Task<IResultModel<bool>> EnabledVersionAsync(string key, int versionId)
    {
        if (key.IsNullOrWhiteSpace())
            return Error<bool>("配置key不能为空");
        if (versionId == 0)
            return Error<bool>("版本ID无效");
        try
        {
            // 清除缓存
            var cacheKey = SettingConfigConst.ConfigPrefix + key;
            await _cache.RemoveAsync(cacheKey);

            var flag = await _dataSourceProvider.EnabledVersionAsync(key, versionId);
            return Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"启用配置报错：{ex.Message} key:{key}");
            return Error<bool>("启用配置出错");
        }
    }

    public async Task<IResultModel<bool>> DeleteConfigAsync(int configId)
    {
        if (configId == 0)
            return Error<bool>("版本ID无效");
        try
        {
            var config = await _dataSourceProvider.GetConfigInfoAsync(configId);
            if (config is null)
                return Error<bool>("配置标识无效");

            var key = SettingConfigConst.ConfigPrefix + config.Key;
            await _cache.RemoveAsync(key);
            await _dataSourceProvider.DeleteConfigAsync(configId);
            return Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除配置报错：{ex.Message} configId:{configId}");
            return Error<bool>("删除配置报错");
        }
    }

    public async Task<string> GetConfigContentAsync(string configKey, bool throwError = true)
    {
        var key = SettingConfigConst.ConfigPrefix + configKey;
        var crConfigContent = await _cache.GetStringAsync(key).ConfigureAwait(false);
        if (crConfigContent != null)
            return crConfigContent;

        var crConfig = await _dataSourceProvider.GetConfigValue(configKey)
            .ConfigureAwait(false);
        if (crConfig.IsNullOrWhiteSpace())
        {
            _logger.LogError("GetConfigContentAsync 根据当前key{Key}没有查询到配置信息", key);
            return null;
        }

        await _cache.SetStringAsync(key, crConfig, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
        }).ConfigureAwait(false);
        return crConfig;
    }

    public async Task<T> GetConfigAsync<T>(string crConfigKey, bool throwError = true)
    {
        try
        {
            var content = await GetConfigContentAsync(crConfigKey, throwError).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(content)) return JsonConvert.DeserializeObject<T>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取配置信息进行反序列化出错 message:{ExMessage} stackTrace:{ExStackTrace}", ex.Message,
                ex.StackTrace);

            if (ex is NotFoundException)
            {
                throw;
            }

            if (throwError)
            {
                throw new ArgumentException("获取配置信息出错");
            }
        }

        return default;
    }
}