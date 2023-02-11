namespace SettingConfig.Domain
{
    /// <summary>
    /// 系统配置版本表
    /// </summary>
    public class SystemConfigVersion
    {
        /// <summary>
        /// 标识列
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 配置key(唯一)
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 版本
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 配置的值
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public string CreateUserId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public string UpdateUserId { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; }

        /// <summary>
        /// 是否禁用
        /// </summary>
        public bool IsDisabled { get; set; }
    }
}