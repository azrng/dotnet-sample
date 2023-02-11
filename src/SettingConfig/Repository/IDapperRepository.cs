namespace SettingConfig.Repository
{
    /// <summary>
    /// dapper接口
    /// </summary>
    public interface IDapperRepository
    {
        /// <summary>
        /// 查询
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        List<T>? Query<T>(string sql, object? param = null);

        /// <summary>
        /// 异步查询
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<List<T>?> QueryAsync<T>(string sql, object? param = null);

        /// <summary>
        /// 查询第一条
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        T QueryFirstOrDefault<T>(string sql, object? param = null);

        /// <summary>
        /// 查询第一条
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<T> QueryFirstOrDefaultAsync<T>(string sql, object? param = null);

        /// <summary>
        /// 查询多条
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<Tuple<IEnumerable<T1>, IEnumerable<T2>>> QueryMultipleAsync<T1, T2>(string sql, object param);

        /// <summary>
        /// 查询多条返回到一个集合
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<IEnumerable<T>> QueryMultipleSameResultSetAsync<T>(string sql, object param);

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        int Execute(string sql, object? param = null);

        /// <summary>
        /// 执行sql
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<int> ExecuteAsync(string sql, object? param = null);

        /// <summary>
        /// 返回首行首列
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        T ExecuteScalar<T>(string sql, object? param = null);

        /// <summary>
        /// 返回首行首列
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        Task<T> ExecuteScalarAsync<T>(string sql, object? param = null);
    }
}