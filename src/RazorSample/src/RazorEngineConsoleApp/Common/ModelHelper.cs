namespace RazorEngineConsoleApp.Common
{
    public static class ModelHelper
    {
        /// <summary>
        /// 首字母小写
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string FirstToLow(string str)
        {
            return str.Substring(0, 1).ToLower() + str.Substring(1);
        }

        /// <summary>
        /// 首字母大写
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string FirstToUp(string str)
        {
            return str.Substring(0, 1).ToUpper() + str.Substring(1);
        }
    }
}