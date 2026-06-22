using System.Data;
using System.Globalization;
using System.Text;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack 协议的批量操作辅助类。
/// 支持批量 INSERT 操作。
/// </summary>
public static class QuackBatchExtensions
{
    /// <summary>
    /// 在单次批量操作中插入多行数据。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="tableName">目标表名。</param>
    /// <param name="columns">要插入的列名数组。</param>
    /// <param name="rows">要插入的数据行集合。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>受影响的行数。</returns>
    public static async Task<int> ExecuteBatchInsertAsync(
        this QuackConnection connection,
        string tableName,
        string[] columns,
        IEnumerable<object?[]> rows,
        CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        var rowsList = rows.ToList();
        if (rowsList.Count == 0)
            return 0;

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(QuoteIdentifier(tableName)).Append(" (");
        sb.Append(string.Join(", ", columns.Select(QuoteIdentifier)));
        sb.Append(") VALUES ");

        AppendRowLiterals(sb, rowsList, 0, rowsList.Count);

        await using var command = connection.CreateCommand();
        command.CommandText = sb.ToString();
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 使用参数化批量插入多行数据。
    /// 对大批量数据更高效。
    /// </summary>
    /// <param name="connection">数据库连接。</param>
    /// <param name="tableName">目标表名。</param>
    /// <param name="columns">要插入的列名数组。</param>
    /// <param name="rows">要插入的数据行集合。</param>
    /// <param name="batchSize">每批插入的行数，默认为 100。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>受影响的行数。</returns>
    public static async Task<int> ExecuteParameterizedBatchInsertAsync(
        this QuackConnection connection,
        string tableName,
        string[] columns,
        IEnumerable<object?[]> rows,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        var rowsList = rows.ToList();
        if (rowsList.Count == 0)
            return 0;

        var quotedTable = QuoteIdentifier(tableName);
        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));

        int totalAffected = 0;

        for (int offset = 0; offset < rowsList.Count; offset += batchSize)
        {
            // Index-based slicing — O(1) per batch. The old Skip(offset).Take(batchSize).ToList()
            // re-enumerated from the start on every batch, making the whole insert O(n²).
            var count = Math.Min(batchSize, rowsList.Count - offset);
            var sb = new StringBuilder();
            sb.Append("INSERT INTO ").Append(quotedTable).Append(" (").Append(quotedColumns).Append(") VALUES ");
            AppendRowLiterals(sb, rowsList, offset, count);

            await using var command = connection.CreateCommand();
            command.CommandText = sb.ToString();
            totalAffected += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return totalAffected;
    }

    private static void AppendRowLiterals(StringBuilder sb, List<object?[]> rows, int start, int count)
    {
        var end = start + count;
        for (int i = start; i < end; i++)
        {
            if (i > start) sb.Append(", ");
            sb.Append("(");
            var row = rows[i];
            for (int j = 0; j < row.Length; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(FormatValue(row[j]));
            }
            sb.Append(")");
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));

        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            DBNull => "NULL",
            bool b => b ? "TRUE" : "FALSE",
            string s => QuoteString(s),
            char c => QuoteString(c.ToString()),
            byte[] bytes => "X'" + Convert.ToHexString(bytes) + "'",
            Guid g => QuoteString(g.ToString()),
            DateTime dt => "TIMESTAMP " + QuoteString(dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => "TIMESTAMPTZ " + QuoteString(dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture)),
            DateOnly d => "DATE " + QuoteString(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            TimeOnly t => "TIME " + QuoteString(t.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "NULL",
            _ => QuoteString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
        };
    }

    private static string QuoteString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }
}
