using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// 将包含参数占位符的 SQL 语句渲染为内联字面量形式，支持位置参数 (?)、命名参数 (@name / :name / $name)。
/// </summary>
internal static class QuackParameterSqlRenderer
{
    /// <summary>
    /// 将 SQL 中的参数占位符替换为对应的字面量值，同时正确跳过字符串、标识符、注释和美元引号。
    /// </summary>
    /// <param name="sql">包含参数占位符的原始 SQL 语句。</param>
    /// <param name="parameters">提供参数值的 <see cref="DbParameterCollection"/>。</param>
    /// <returns>所有占位符已替换为字面量的 SQL 字符串。</returns>
    public static string Render(string sql, DbParameterCollection parameters)
    {
        if (parameters.Count == 0)
            return sql;

        var named = new Dictionary<string, DbParameter>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DbParameter>(parameters.Count);

        foreach (DbParameter parameter in parameters)
        {
            ordered.Add(parameter);

            var name = NormalizeParameterName(parameter.ParameterName);
            if (!string.IsNullOrWhiteSpace(name))
                named[name] = parameter;
        }

        var builder = new StringBuilder(sql.Length + parameters.Count * 8);
        var positionalIndex = 0;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];

            if (current == '\'')
            {
                AppendSingleQuotedString(sql, builder, ref i);
                continue;
            }

            if (current == '"')
            {
                AppendDoubleQuotedIdentifier(sql, builder, ref i);
                continue;
            }

            if (current == '$')
            {
                if (TryAppendDollarQuote(sql, builder, ref i))
                    continue;
            }

            if (current == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                AppendLineComment(sql, builder, ref i);
                continue;
            }

            if (current == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                AppendBlockComment(sql, builder, ref i);
                continue;
            }

            if (current == '?')
            {
                if (positionalIndex >= ordered.Count)
                    throw new InvalidOperationException("No parameter value was supplied for positional placeholder '?'.");

                builder.Append(ToSqlLiteral(ordered[positionalIndex++].Value));
                continue;
            }

            if (IsNamedParameterPrefix(current, sql, i))
            {
                var nameStart = i + 1;
                if (nameStart < sql.Length && IsParameterNameStart(sql[nameStart]))
                {
                    var nameEnd = nameStart + 1;
                    while (nameEnd < sql.Length && IsParameterNamePart(sql[nameEnd]))
                        nameEnd++;

                    var name = sql[nameStart..nameEnd];
                    if (!named.TryGetValue(name, out var parameter))
                        throw new InvalidOperationException($"No parameter value was supplied for '{current}{name}'.");

                    builder.Append(ToSqlLiteral(parameter.Value));
                    i = nameEnd - 1;
                    continue;
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool IsNamedParameterPrefix(char current, string sql, int index)
    {
        if (current is '@' or '$')
            return true;

        if (current != ':')
            return false;

        return index == 0 || sql[index - 1] != ':';
    }

    private static bool IsParameterNameStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsParameterNamePart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static string NormalizeParameterName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var trimmed = name.Trim();
        return trimmed[0] is '@' or ':' or '$' or '?' ? trimmed[1..] : trimmed;
    }

    private static void AppendSingleQuotedString(string sql, StringBuilder builder, ref int index)
    {
        builder.Append(sql[index]);
        index++;

        while (index < sql.Length)
        {
            builder.Append(sql[index]);
            if (sql[index] == '\'')
            {
                if (index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    index++;
                    builder.Append(sql[index]);
                }
                else
                {
                    break;
                }
            }

            index++;
        }
    }

    private static void AppendDoubleQuotedIdentifier(string sql, StringBuilder builder, ref int index)
    {
        builder.Append(sql[index]);
        index++;

        while (index < sql.Length)
        {
            builder.Append(sql[index]);
            if (sql[index] == '"')
            {
                if (index + 1 < sql.Length && sql[index + 1] == '"')
                {
                    index++;
                    builder.Append(sql[index]);
                }
                else
                {
                    break;
                }
            }

            index++;
        }
    }

    private static void AppendLineComment(string sql, StringBuilder builder, ref int index)
    {
        while (index < sql.Length)
        {
            builder.Append(sql[index]);
            if (sql[index] == '\n')
                break;

            index++;
        }
    }

    private static void AppendBlockComment(string sql, StringBuilder builder, ref int index)
    {
        builder.Append(sql[index++]);
        builder.Append(sql[index]);

        while (++index < sql.Length)
        {
            builder.Append(sql[index]);
            if (sql[index] == '/' && index > 0 && sql[index - 1] == '*')
                break;
        }
    }

    private static bool TryAppendDollarQuote(string sql, StringBuilder builder, ref int index)
    {
        if (index + 1 >= sql.Length)
            return false;

        var next = sql[index + 1];
        string tag;

        if (next == '$')
        {
            tag = "$$";
        }
        else if (IsParameterNameStart(next))
        {
            var tagEnd = index + 2;
            while (tagEnd < sql.Length && IsParameterNamePart(sql[tagEnd]))
                tagEnd++;

            if (tagEnd >= sql.Length || sql[tagEnd] != '$')
                return false;

            tag = sql[index..(tagEnd + 1)];
        }
        else
        {
            return false;
        }

        builder.Append(tag);
        index += tag.Length;

        while (index < sql.Length)
        {
            builder.Append(sql[index]);
            if (sql[index] == '$' && index + tag.Length <= sql.Length && sql.AsSpan(index, tag.Length).SequenceEqual(tag.AsSpan()))
            {
                for (var j = 1; j < tag.Length; j++)
                {
                    index++;
                    builder.Append(sql[index]);
                }

                return true;
            }

            index++;
        }

        return true;
    }

    private static string ToSqlLiteral(object? value)
    {
        if (value is null or DBNull)
            return "NULL";

        if (value is Enum enumValue)
            value = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), CultureInfo.InvariantCulture);

        return value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            string s => QuoteString(s),
            char c => QuoteString(c.ToString()),
            byte[] bytes => ToBlobLiteral(bytes),
            Guid guid => QuoteString(guid.ToString()),
            DateTime dateTime => $"TIMESTAMP {QuoteString(dateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture))}",
            // DuckDB's TIMESTAMPTZ literal requires an IANA/abbreviated zone, not a numeric
            // offset ("+08:00" is rejected as an unknown time zone). Normalise to UTC and tag
            // the value so it round-trips as the same instant.
            DateTimeOffset dateTimeOffset => $"TIMESTAMPTZ {QuoteString(dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + " UTC")}",
            DateOnly dateOnly => $"DATE {QuoteString(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            TimeOnly timeOnly => $"TIME {QuoteString(timeOnly.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture))}",
            float f => ToFloatingPointLiteral(f),
            double d => ToFloatingPointLiteral(d),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable when IsNumeric(value) => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "NULL",
            IEnumerable enumerable when value is not string => ToListLiteral(enumerable),
            _ => QuoteString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
        };
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    private static string ToFloatingPointLiteral(float value)
    {
        if (float.IsNaN(value))
            return "CAST('NaN' AS FLOAT)";

        if (float.IsPositiveInfinity(value))
            return "CAST('Infinity' AS FLOAT)";

        if (float.IsNegativeInfinity(value))
            return "CAST('-Infinity' AS FLOAT)";

        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static string ToFloatingPointLiteral(double value)
    {
        if (double.IsNaN(value))
            return "CAST('NaN' AS DOUBLE)";

        if (double.IsPositiveInfinity(value))
            return "CAST('Infinity' AS DOUBLE)";

        if (double.IsNegativeInfinity(value))
            return "CAST('-Infinity' AS DOUBLE)";

        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string QuoteString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static string ToBlobLiteral(byte[] bytes)
    {
        // DuckDB has no X'..' hex literal; binary literals use the string-escape form
        // '\xHH\xHH..' where each byte is escaped as \x followed by two hex digits.
        var sb = new StringBuilder(bytes.Length * 4 + 3);
        sb.Append('\'');
        foreach (var b in bytes)
        {
            sb.Append("\\x");
            sb.Append(b.ToString("X2"));
        }
        sb.Append('\'');
        return sb.ToString();
    }

    private static string ToListLiteral(IEnumerable values)
    {
        var literals = new List<string>();
        foreach (var value in values)
            literals.Add(ToSqlLiteral(value));

        return "(" + string.Join(", ", literals) + ")";
    }
}
