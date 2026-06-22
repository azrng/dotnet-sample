using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// 表示 SQL 查询的执行结果，包含查询标识、列信息、数据行及分页状态。
/// </summary>
/// <param name="QueryId">查询的唯一标识符。</param>
/// <param name="Columns">结果集中各列的元数据信息。</param>
/// <param name="Rows">结果集中的数据行，每行为一个对象数组。</param>
/// <param name="HasMoreRows">指示是否存在更多待拉取的数据行。</param>
public sealed record QuackQueryResult(
    string QueryId,
    IReadOnlyList<QuackColumnInfo> Columns,
    IReadOnlyList<object?[]> Rows,
    bool HasMoreRows)
{
    /// <summary>
    /// Opaque token used by the bridge to fetch additional batches. Null when no further pages exist.
    /// </summary>
    public string? FetchToken { get; init; }

    /// <summary>
    /// Columnar batch storage produced by the protocol bridge. When present, the data reader
    /// accesses columns directly via <c>Batch.Columns[ordinal][row]</c>, avoiding row-materialisation
    /// overhead. Null when the result was produced without columnar decoding (e.g. fake bridges in tests).
    /// </summary>
    internal ColumnarBatch? Batch { get; init; }
}
