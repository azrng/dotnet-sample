# SettingConfig

该项目是一个业务配置维护的Nuget包

# 使用场景

安装该包后，通过简单对接数据库，就可以实现业务逻辑配置的数据库存储，支持页面修改、多版本控制，代码中查询以及缓存处理。

在每次版本升级之后，不会直接删除客户的自定义配置，而是默认启用最新版本的配置，客户可以自行比对后进行个性化配置。

配置的版本跟着项目版本走，可以方便升级问题可以进行回退版本(如果项目从1.0.1版本回退到1.0.0，那么就搜索1.0.0版本然后全部启用当前版本即可回退配置)

# 接入方案

注入服务配置

```
builder.Services.AddSettingConfig(options =>
{
    options.DbConnection = dbConnection;
    options.DbSchema = "sample";
});
```

使用ui界面

```
app.UseSettingUI();
```

默认情况下启动项目访问 url/systemsetting 进行访问

项目中如果需要查询配置信息进行注入服务IConfigSettingService

```
var aa = await _configSettingService.GetConfigContentAsync("aaa");
// 或者
var aa = await _configSettingService.GetConfigAsync<List<string>>("aaa");
```

# 数据库表结构

> 注意：表结构创建的功能已经代码实现，下面只是为了展示

配置表：包含基本的配置信息

```
create table if not exists sample.system_config
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
comment on table sample.system_config is '系统配置表';
comment on column sample.system_config.id is '标识列';
comment on column sample.system_config.key is '配置key';
comment on column sample.system_config.name is '配置名';
comment on column sample.system_config.create_user_id is '创建人标识';
comment on column sample.system_config.create_time is '创建时间';
comment on column sample.system_config.is_deleted is '是否删除';
create unique index system_config_key_uindex
    on sample.system_config (key);
```

配置版本表：配置表对应的版本详情，同一个key只能有一个启用的配置

```
create table if not exists sample.system_config_version
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
comment on table sample.system_config_version is '系统配置版本表';
comment on column sample.system_config_version.id is '标识列';
comment on column sample.system_config_version.key is '配置key';
comment on column sample.system_config_version.value is '配置值';
comment on column sample.system_config_version.description is '描述信息';
comment on column sample.system_config_version.version is '版本标识';
comment on column sample.system_config_version.create_user_id is '创建人ID';
comment on column sample.system_config_version.create_time is '创建时间';
comment on column sample.system_config_version.update_user_id is '更新人id';
comment on column sample.system_config_version.update_time is '更新时间';
comment on column sample.system_config_version.is_disabled is '是否禁用';
create unique index system_config_version_key_is_disabled_uindex
    on sample.system_config_version (key, is_disabled);;
create unique index system_config_version_version_uindex
    on sample.system_config_version (version);
```

# 功能

* 个性化配置

  - [x] 页面标题设置
  - [x] 页面路由设置
  - [ ] 页面加密访问

* 系统配置界面

  - [x] 列表展示
    - [x] 配置名、配置key搜索

    - [ ] 版本筛选

    - [x] 删除配置

  - [x] 配置编辑

  - [x] 版本列表
    - [x] 启用指定版本

* 存储
  - [x] pgsql存储
* 使用
  - [x] 项目中查询
  - [x] 查询缓存

# 扩展

## 缓存扩展

该项目默认使用内存缓存进行存储，你可以自行继承接口来替换默认的缓存方案。

# 欠缺

* 目前只能编辑当前启用版本的配置，不支持编辑未启用版本
* 每个版本下面应该包含变更记录，然后可以在当前版本下选择