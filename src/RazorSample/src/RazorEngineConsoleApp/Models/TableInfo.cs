using System.Collections.Generic;

namespace RazorEngineConsoleApp.Models
{
    public class TableInfo
    {
        /// <summary>
        /// 表名
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// 参数字段
        /// </summary>
        public List<DbParamInfo> Parameters { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Desc { get; set; }
    }

    /// <summary>
    /// 数据库参数
    /// </summary>
    public class DbParamInfo
    {
        /// <summary>
        /// 字段名称
        /// </summary>
        private string paramName;

        /// <summary>
        /// 字段类型
        /// </summary>
        private string paramType;

        /// <summary>
        /// 字段描述
        /// </summary>
        private string desc;

        public string ParamName
        {
            get { return paramName; }
            set { paramName = value; }
        }

        public string ParamType
        {
            get { return paramType; }
            set { paramType = value; }
        }

        public string Desc
        {
            get { return desc; }
            set { desc = value; }
        }
    }
}