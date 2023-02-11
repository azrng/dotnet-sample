using System.Text;
using Common.Core.Extension;
using Microsoft.Extensions.Logging;
using SettingConfig.Dto;
using SettingConfig.Repository;

namespace SettingConfig;

/// <summary>
/// pgsql存储提供者
/// </summary>
public class PgsqlDataSourceProvider : IDataSourceProvider
{
    private readonly SettingUIOptions _options;
    private readonly IDapperRepository _dapperRepository;
    private readonly ILogger<PgsqlDataSourceProvider> _logger;

    public PgsqlDataSourceProvider(IDapperRepository dapperRepository, ILogger<PgsqlDataSourceProvider> logger)
    {
        _dapperRepository = dapperRepository;
        _logger = logger;
        _options = ServiceCollectionExtensions.injectUIOptions;
    }

    public async Task<bool> InitAsync()
    {
        var checkExist = $@"SELECT count(1)
FROM pg_class a
LEFT OUTER JOIN pg_description b ON b.objsubid = 0 AND a.oid = b.objoid
WHERE a.relnamespace = (SELECT oid FROM pg_namespace WHERE nspname = '{_options.DbSchema}')
AND a.relkind = 'r' and a.relname='system_config';";

        _logger.LogInformation($"queryDb  检查表是否存在SQL：{checkExist}");
        var num = await _dapperRepository.ExecuteScalarAsync<int>(checkExist);
        if (num > 0)
            return true;

        var sb = new StringBuilder($"CREATE SCHEMA IF NOT EXISTS {_options.DbSchema};");
        // 创建配置表
        sb.Append($@"create table if not exists {_options.DbSchema}.system_config
(
    id             serial
        constraint system_config_pk
            primary key,
    key varchar(100) not null,
    name varchar(100)  not null,
    create_user_id varchar(50) default '' not null,
    create_time    timestamp       not null,
    is_deleted     bool            not null
);
comment on table {_options.DbSchema}.system_config is '系统配置表';
comment on column {_options.DbSchema}.system_config.id is '标识列';
comment on column {_options.DbSchema}.system_config.key is '配置key';
comment on column {_options.DbSchema}.system_config.name is '配置名';
comment on column {_options.DbSchema}.system_config.create_user_id is '创建人标识';
comment on column {_options.DbSchema}.system_config.create_time is '创建时间';
comment on column {_options.DbSchema}.system_config.is_deleted is '是否删除';
create unique index system_config_key_uindex
    on {_options.DbSchema}.system_config (key);");
        // 创建配置版本表
        sb.Append($@"create table if not exists {_options.DbSchema}.system_config_version
(
    id             serial         not null
        constraint system_config_version_pk
            primary key,
    key            varchar(100)    not null,
    value          text            not null,
    description    text default '' not null,
    version        varchar(50)     not null,
    create_user_id varchar(50) default ''   not null,
    create_time    timestamp       not null,
    update_user_id varchar(50) default ''   not null,
    update_time    timestamp       not null,
    is_disabled    bool            not null
);
comment on table {_options.DbSchema}.system_config_version is '系统配置版本表';
comment on column {_options.DbSchema}.system_config_version.id is '标识列';
comment on column {_options.DbSchema}.system_config_version.key is '配置key';
comment on column {_options.DbSchema}.system_config_version.value is '配置值';
comment on column {_options.DbSchema}.system_config_version.description is '描述信息';
comment on column {_options.DbSchema}.system_config_version.version is '版本标识';
comment on column {_options.DbSchema}.system_config_version.create_user_id is '创建人ID';
comment on column {_options.DbSchema}.system_config_version.create_time is '创建时间';
comment on column {_options.DbSchema}.system_config_version.update_user_id is '更新人id';
comment on column {_options.DbSchema}.system_config_version.update_time is '更新时间';
comment on column {_options.DbSchema}.system_config_version.is_disabled is '是否禁用';
create unique index system_config_version_version_uindex
    on {_options.DbSchema}.system_config_version (version, key);");
        _logger.LogInformation($"queryDb  创建表SQL：{sb}");
        return await _dapperRepository.ExecuteAsync(sb.ToString()) > 0;
    }

    public async Task<List<GetSettingInfoDto>> GetPageListAsync(int pageIndex, int pageSize, string keyword,
        string version)
    {
        var sb = new StringBuilder(
            $@"with configversion  as (select * from {_options.DbSchema}.system_config_version where is_disabled=false)");
        sb.Append(
                $@"select  config.id,config.key,config.name,configversion.value,configversion.description,configversion.version,configversion.id versionid
        from {_options.DbSchema}.system_config config inner join configversion on config.key=configversion.key
        where config.is_deleted=false")
            .AppendIF(keyword.IsNotNullOrWhiteSpace(),
                " and (config.key like @keyword or config.name like @keyword or configversion.description like @keyword)")
            .AppendIF(version.IsNotNullOrWhiteSpace(), " and configversion.version='@version'")
            .Append($@" order by config.create_time desc limit {pageSize} offset  {pageSize * (pageIndex - 1)};");
        _logger.LogInformation($"SQL：{sb}");
        return await _dapperRepository.QueryAsync<GetSettingInfoDto>(sb.ToString(),
            new { keyword = $"%{keyword}%", version });
    }

    public async Task<int> GetConfigCount()
    {
        var sql = $"select count(key) from {_options.DbSchema}.system_config where is_deleted=false";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteScalarAsync<int>(sql);
    }

    public async Task<GetConfigDetailsResult> GetConfigDetails(int configId)
    {
        var sql =
            $@"with configversion  as (select * from {_options.DbSchema}.system_config_version where is_disabled=false)
select config.key,config.name,configversion.value,configversion.description,configversion.version,configversion.id versionid
from {_options.DbSchema}.system_config config inner join configversion on config.key=configversion.key
                     where config.is_deleted=false and config.id={configId}";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.QueryFirstOrDefaultAsync<GetConfigDetailsResult>(sql);
    }

    public async Task<GetConfigInfoDto> GetConfigInfoAsync(int configId)
    {
        var sql = $"select key,name from {_options.DbSchema}.system_config where id=@id";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.QueryFirstOrDefaultAsync<GetConfigInfoDto>(sql, new { id = configId });
    }

    public async Task<string> GetConfigKeyAsync(int configVersionId)
    {
        var sql = $"select key from {_options.DbSchema}.system_config_version where id=@id";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteScalarAsync<string>(sql, new { id = configVersionId });
    }

    public async Task<bool> UpdateConfigVersionAsync(int versionId, string value, string description,
        string updateUserId)
    {
        var sql =
            $"update {_options.DbSchema}.system_config_version set value=@value,description=@description,update_time=@update_time,update_user_id=@update_user_id where id=@versionId";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteAsync(sql,
            new { versionId, value, description, update_time = DateTime.Now, update_user_id = updateUserId }) > 0;
    }

    public async Task<List<GetConfigVersionListResult>> GetConfigVersionListAsync(string key)
    {
        var sql =
            $@"select id versionId,key,value,description,version,create_time createTime,update_time updateTime,is_disabled isDisabled
        from {_options.DbSchema}.system_config_version where key=@key  order by  create_time desc ;";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.QueryAsync<GetConfigVersionListResult>(sql, new { key });
    }

    public async Task<bool> EnabledVersionAsync(string key, int versionId)
    {
        var sql = $"update {_options.DbSchema}.system_config_version set is_disabled=(id!=@versionId)  where key=@key";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteAsync(sql, new { key, versionId }) > 0;
    }

    public async Task<bool> DeleteConfigAsync(int configId)
    {
        var sql = $"update {_options.DbSchema}.system_config set is_deleted=true where id=@configId";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteAsync(sql, new { configId }) > 0;
    }

    public async Task<string> GetConfigValue(string key)
    {
        var sql =
            $@"with configversion  as (select * from {_options.DbSchema}.system_config_version where is_disabled=false)
select configversion.value
from {_options.DbSchema}.system_config config inner join configversion on config.key=configversion.key
                     where config.is_deleted=false and config.key=@key";
        _logger.LogInformation($"SQL：{sql}");
        return await _dapperRepository.ExecuteScalarAsync<string>(sql, new { key });
    }
}