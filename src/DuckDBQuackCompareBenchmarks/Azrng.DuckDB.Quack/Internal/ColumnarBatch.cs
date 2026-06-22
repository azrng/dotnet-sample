namespace Azrng.DuckDB.Quack.Internal;

/// <summary>
/// 列的物理存储类型。决定 <see cref="ColumnarBatch.Columns"/> 中对应列的元素类型，
/// 使值类型列以原生数组存储（<c>long[]</c>、<c>double[]</c> 等），避免逐值装箱。
/// </summary>
internal enum QuackColumnKind : byte
{
    /// <summary>异构回退（HUGEINT/UNKNOWN）：列为 <c>object?[]</c>，仍按值装箱。</summary>
    Object,
    Int64,
    Double,
    Single,
    Boolean,
    String,
    Blob,
    Decimal,
    Guid,
    DateTime,
    Date,
}

/// <summary>
/// 查询结果的列式存储。每列存储为原生 typed 数组（<c>long[]</c>/<c>double[]</c>/<c>string?[]</c>…），
/// 配合 null 位图，避免旧实现 <c>List&lt;object?&gt;</c> 对值类型的逐元素装箱。
/// 支持按列直接索引访问，无需行转置。
/// </summary>
internal sealed class ColumnarBatch
{
    /// <summary>此批次中的行数。</summary>
    public int RowCount { get; }

    /// <summary>此批次中的列数。</summary>
    public int ColumnCount => ColumnKinds.Length;

    /// <summary>每列的类型标识。</summary>
    public QuackColumnKind[] ColumnKinds { get; }

    /// <summary>
    /// 列主序 typed 数组。<c>Columns[col]</c> 得到 <c>long[]</c>/<c>double[]</c>/<c>string?[]</c> 等；
    /// <c>null</c> 表示整列为空。元素不再装箱。
    /// </summary>
    public Array?[] Columns { get; }

    /// <summary>
    /// 每列的 null 位图（按位，bit=1 表示非 null）。
    /// <c>null</c> 表示该列无 null（无需检查）。对值类型列，null 行在数组中为默认值，由位图区分。
    /// </summary>
    public byte[]?[] Validity { get; }

    public ColumnarBatch(QuackColumnKind[] columnKinds, Array?[] columns, byte[]?[] validity, int rowCount)
    {
        ColumnKinds = columnKinds;
        Columns = columns;
        Validity = validity;
        RowCount = rowCount;
    }

    /// <summary>指定列、行的值是否为 null（位图对应位为 0）。</summary>
    public bool IsNull(int col, int row)
    {
        var bits = Validity[col];
        return bits is not null && ((bits[row >> 3] >> (row & 7)) & 1) == 0;
    }

    /// <summary>
    /// 读取单元格并装箱。仅在调用方确实需要 <see cref="object"/> 时付出装箱代价；
    /// 未被读取的值零分配。对值类型列优先使用 <see cref="GetInt64"/> 等 typed 访问器。
    /// </summary>
    public object? GetValue(int col, int row)
    {
        if (IsNull(col, row)) return null;
        var arr = Columns[col];
        return arr is null ? null : arr.GetValue(row);
    }

    /// <summary>整型列的快路径；非整型列回退到转换，保持与旧行为一致。</summary>
    public long GetInt64(int col, int row)
    {
        if (Columns[col] is long[] a) return a[row];
        return Convert.ToInt64(GetValue(col, row));
    }

    /// <summary>字符串列的快路径；null 由位图判定。</summary>
    public string? GetString(int col, int row)
    {
        if (IsNull(col, row)) return null;
        if (Columns[col] is string?[] s) return s[row];
        var v = GetValue(col, row);
        return v is null ? null : Convert.ToString(v);
    }
}
