using System.Data;
using Dapper;

namespace SettingConfig.Repository
{
    /// <summary>
    /// dapper仓储的基类
    /// </summary>
    public class DapperRepository : IDapperRepository
    {
        /// <summary>
        /// 数据库链接
        /// </summary>
        private readonly IDbConnection _dbConnection;

        public DapperRepository(IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public List<T>? Query<T>(string sql, object? param = null)
        {
            return _dbConnection.Query<T>(sql, param)?.ToList();
        }

        public async Task<List<T>?> QueryAsync<T>(string sql, object? param = null)
        {
            return (await _dbConnection.QueryAsync<T>(sql, param))?.ToList();
        }

        public T QueryFirstOrDefault<T>(string sql, object? param = null)
        {
            return _dbConnection.QueryFirstOrDefault<T>(sql, param);
        }

        public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
        {
            return await _dbConnection.QueryFirstOrDefaultAsync<T>(sql, param);
        }

        public async Task<Tuple<IEnumerable<T1>, IEnumerable<T2>>> QueryMultipleAsync<T1, T2>(string sql, object param)
        {
            var result = await _dbConnection.QueryMultipleAsync(sql, param);
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>>(await result.ReadAsync<T1>(), await result.ReadAsync<T2>());
        }

        public async Task<IEnumerable<T>> QueryMultipleSameResultSetAsync<T>(string sql, object param)
        {
            var resultList = new List<T>();
            var multi = await _dbConnection.QueryMultipleAsync(sql, param);
            //遍历结果集
            while (!multi.IsConsumed)
            {
                var result = await multi.ReadAsync<T>();
                if (result?.Any() == true)
                {
                    resultList.AddRange(result);
                }
            }
            return resultList;
        }

        public int Execute(string sql, object? param = null)
        {
            return _dbConnection.Execute(sql, param);
        }

        public async Task<int> ExecuteAsync(string sql, object? param = null)
        {
            return await _dbConnection.ExecuteAsync(sql, param);
        }

        public T ExecuteScalar<T>(string sql, object? param = null)
        {
            return _dbConnection.ExecuteScalar<T>(sql, param);
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null)
        {
            return await _dbConnection.ExecuteScalarAsync<T>(sql, param);
        }
    }
}