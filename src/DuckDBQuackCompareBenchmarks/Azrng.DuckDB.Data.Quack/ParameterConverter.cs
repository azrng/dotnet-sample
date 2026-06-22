using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using DuckDB.NET.Data;

namespace Azrng.DuckDB.Data.Quack;

/// <summary>
/// 参数转换器，将 @paramName 命名参数转换为 DuckDB 支持的位置参数
/// DuckDB 只支持 ? 或 $1,$2 位置参数，不支持 @paramName
/// </summary>
public static partial class ParameterConverter
{
    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex NamedParamRegex();

    /// <summary>
    /// 将 @paramName 风格的命名参数转换为 ? 位置参数
    /// 返回转换后的 SQL 和对应的 DuckDBParameter 数组
    /// </summary>
    public static (string Sql, DuckDBParameter[] Parameters) ConvertNamedToQuestionMark(string sql, object parameters)
    {
        if (parameters == null)
            return (sql, []);

        var paramDict = ToParamDictionary(parameters);
        var ordered = new List<string>();

        var converted = NamedParamRegex().Replace(sql, match =>
        {
            var name = match.Groups[1].Value;
            if (!ordered.Contains(name))
                ordered.Add(name);
            return "?";
        });

        var dbParams = ordered.Select(p =>
        {
            var val = paramDict.TryGetValue(p, out var v) ? v : null;
            return new DuckDBParameter { Value = val ?? DBNull.Value };
        }).ToArray();

        return (converted, dbParams);
    }

    /// <summary>
    /// 将 @paramName 风格的命名参数转换为 $1, $2 位置参数
    /// 返回转换后的 SQL 和对应的 DynamicParameters（Dapper）
    /// </summary>
    public static (string Sql, DynamicParameters? Parameters) ConvertNamedToPositional(string sql, object? parameters)
    {
        if (parameters == null)
            return (sql, null);

        var paramNames = new List<string>();
        var positionalSql = NamedParamRegex().Replace(sql, match =>
        {
            var name = match.Groups[1].Value;
            var index = paramNames.IndexOf(name);
            if (index < 0)
            {
                paramNames.Add(name);
                index = paramNames.Count - 1;
            }
            return $"${index + 1}";
        });

        if (paramNames.Count == 0)
            return (sql, null);

        var dynamicParams = new DynamicParameters();
        if (parameters is IDictionary<string, object> dict)
        {
            for (var i = 0; i < paramNames.Count; i++)
            {
                var name = paramNames[i];
                if (!dict.TryGetValue(name, out var value))
                    throw new ArgumentException($"参数 '{name}' 未提供", nameof(parameters));

                dynamicParams.Add((i + 1).ToString(CultureInfo.InvariantCulture), value);
            }
        }
        else
        {
            var type = parameters.GetType();
            for (var i = 0; i < paramNames.Count; i++)
            {
                var name = paramNames[i];
                var prop = type.GetProperty(name);
                if (prop == null)
                    throw new ArgumentException($"参数 '{name}' 未提供", nameof(parameters));

                dynamicParams.Add((i + 1).ToString(CultureInfo.InvariantCulture), prop.GetValue(parameters));
            }
        }

        return (positionalSql, dynamicParams);
    }

    /// <summary>
    /// 将 @paramName 命名参数解析为 SQL 字面量；用于 quack_query 远端 SQL 字符串包装。
    /// </summary>
    public static string ResolveNamedParameters(string sql, object? parameters)
    {
        if (parameters == null)
            return sql;

        var paramDict = ToParamDictionary(parameters);
        return NamedParamRegex().Replace(sql, match =>
        {
            var name = match.Groups[1].Value;
            if (!TryGetParameterValue(paramDict, name, out var value))
                throw new ArgumentException($"参数 '{name}' 未提供", nameof(parameters));

            return FormatParameterValue(value);
        });
    }

    /// <summary>
    /// 将 ? 位置参数按顺序解析为 SQL 字面量；用于 quack_query 远端 SQL 字符串包装。
    /// </summary>
    public static string ResolveQuestionMarkParameters(string sql, IReadOnlyList<DuckDBParameter> parameters)
    {
        if (parameters.Count == 0)
            return sql;

        var parameterIndex = 0;
        var resolved = Regex.Replace(sql, @"\?", _ =>
        {
            if (parameterIndex >= parameters.Count)
                throw new ArgumentException("SQL 中的 ? 参数数量超过已提供参数数量。", nameof(parameters));

            return FormatParameterValue(parameters[parameterIndex++].Value);
        });

        if (parameterIndex < parameters.Count)
            throw new ArgumentException("已提供参数数量超过 SQL 中的 ? 参数数量。", nameof(parameters));

        return resolved;
    }

    private static Dictionary<string, object?> ToParamDictionary(object parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (parameters is IDictionary<string, object> dictObj)
        {
            foreach (var kv in dictObj)
                dict[kv.Key] = kv.Value;
        }
        else
        {
            foreach (var prop in parameters.GetType().GetProperties())
                dict[prop.Name] = prop.GetValue(parameters);
        }

        return dict;
    }

    private static bool TryGetParameterValue(Dictionary<string, object?> parameters, string name, out object? value)
    {
        return parameters.TryGetValue(name, out value) ||
               parameters.TryGetValue($"@{name}", out value) ||
               parameters.TryGetValue($"${name}", out value);
    }

    private static string FormatParameterValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            DBNull => "NULL",
            string s => $"'{EscapeSqlLiteral(s)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss}'",
            bool b => b ? "TRUE" : "FALSE",
            byte or sbyte or short or ushort or int or uint or long or ulong => value.ToString() ?? "NULL",
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            decimal m => m.ToString("G", CultureInfo.InvariantCulture),
            Guid g => $"'{g:D}'",
            byte[] bytes => $"'{Convert.ToHexString(bytes)}'",
            _ => $"'{EscapeSqlLiteral(value.ToString() ?? string.Empty)}'"
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "''");
    }
}
