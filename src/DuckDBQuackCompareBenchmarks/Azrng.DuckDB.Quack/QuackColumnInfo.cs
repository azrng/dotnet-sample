namespace Azrng.DuckDB.Quack;

/// <summary>
/// 查询结果列的元数据信息，包含列名、类型名称和对应的 CLR 类型。
/// </summary>
/// <param name="Name">列名称。</param>
/// <param name="TypeName">列的 DuckDB 类型名称。</param>
/// <param name="FieldType">列对应的 .NET 类型。</param>
public sealed record QuackColumnInfo(string Name, string TypeName, Type FieldType);
