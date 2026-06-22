using System.Text.RegularExpressions;

namespace Azrng.DuckDB.Data.Quack;

/// <summary>
/// SQL 规范化器，处理 DuckDB 特有的 SQL 兼容性问题
/// </summary>
public static partial class SqlNormalizer
{
    [GeneratedRegex(@"\b(?<schema>[A-Za-z_][A-Za-z0-9_]*)\.(?<table>[A-Za-z_][A-Za-z0-9_]*)\.(?<column>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex ThreePartReferenceRegex();

    [GeneratedRegex(@"\b(?:from|join)\s+(?<schema>[A-Za-z_][A-Za-z0-9_]*)\.(?<table>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SchemaTableReferenceRegex();

    /// <summary>
    /// 三段式列引用规范化：schema.table.column -> table.column
    /// 保留 FROM/JOIN 子句中的三段式表引用
    /// </summary>
    public static string NormalizeColumnReferences(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var schemaTables = GetSchemaTables(sql);

        return ThreePartReferenceRegex().Replace(sql, match =>
        {
            var key = $"{match.Groups["schema"].Value}.{match.Groups["table"].Value}";
            if (schemaTables.Count == 1 && schemaTables.Contains(key))
                return match.Groups["column"].Value;

            return match.Value;
        });
    }

    /// <summary>
    /// 将 default. 替换为 main.（DuckDB 的默认 schema）
    /// </summary>
    public static string NormalizeSchemaPrefix(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        return sql.Replace("default.", "main.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 组合规范化：先做 schema 前缀替换，再做列引用规范化
    /// </summary>
    public static string Normalize(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var schemaNormalized = NormalizeSchemaPrefix(sql);
        return NormalizeColumnReferences(schemaNormalized);
    }

    private static HashSet<string> GetSchemaTables(string sql)
    {
        var schemaTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in SchemaTableReferenceRegex().Matches(sql))
        {
            schemaTables.Add($"{match.Groups["schema"].Value}.{match.Groups["table"].Value}");
        }

        return schemaTables;
    }
}
