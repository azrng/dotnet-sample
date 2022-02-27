using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NetCoreFilterSample
{
    /// <summary>
    /// 全局配置
    /// 如果考虑到domain层去适配不同的应用层,则全局配置应该放在应用层
    /// 否则全局配置可以放在领域层
    /// </summary>
    public class GlobalConst
    {
        /// <summary>
        ///     系统默认时间展示格式，1970-01-01 01:01:01
        ///     这是整个系统默认配置的格式，任何其他单独业务的时间格式都不要配置在这个类中
        /// </summary>
        public const string DefaultDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// 默认null
        /// </summary>
        public const string Null = "NULL";

        /// <summary>
        /// 默认响应的json序列化方式
        /// </summary>
        public static JsonSerializerSettings DefaultResponseJsonSerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
    }
}
