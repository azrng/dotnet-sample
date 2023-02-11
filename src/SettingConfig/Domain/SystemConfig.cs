namespace SettingConfig.Domain
{
    /// <summary>
    /// 系统配置表
    /// </summary>
    public class SystemConfig
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
        /// 创建人
        /// </summary>
        public string CreateUserId { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }
    }
}