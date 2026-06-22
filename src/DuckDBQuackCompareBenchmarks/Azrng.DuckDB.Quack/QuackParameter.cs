using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// 表示 DuckDB 数据库命令的参数，继承自 <see cref="DbParameter"/>。
/// </summary>
public sealed class QuackParameter : DbParameter
{
    /// <summary>
    /// 获取或设置参数的名称。
    /// </summary>
    [AllowNull]
    public override string ParameterName { get; set; } = "";

    /// <summary>
    /// 获取或设置参数的值。
    /// </summary>
    public override object? Value { get; set; }

    /// <summary>
    /// 获取或设置参数的数据库类型。
    /// </summary>
    public override DbType DbType { get; set; } = DbType.Object;

    /// <summary>
    /// 获取或设置参数的最大大小（字节数）。
    /// </summary>
    public override int Size { get; set; }

    /// <summary>
    /// 获取或设置参数的方向（输入、输出、双向或返回值）。
    /// </summary>
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <summary>
    /// 获取或设置参数是否可接受空值。
    /// </summary>
    public override bool IsNullable { get; set; }

    /// <summary>
    /// 获取或设置参数值的精度（有效数字位数）。
    /// </summary>
    public override byte Precision { get; set; }

    /// <summary>
    /// 获取或设置参数值的小数位数。
    /// </summary>
    public override byte Scale { get; set; }

    /// <summary>
    /// 获取或设置参数所映射的源列名称。
    /// </summary>
    [AllowNull]
    public override string SourceColumn { get; set; } = "";

    /// <summary>
    /// 获取或设置一个值，指示是否将源列中的空值映射为 <see cref="DBNull"/>。
    /// </summary>
    public override bool SourceColumnNullMapping { get; set; }

    /// <summary>
    /// 获取或设置在加载 <see cref="DataRow"/> 时使用的版本。
    /// </summary>
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

    /// <summary>
    /// 将参数的数据库类型重置为 <see cref="DbType.Object"/>。
    /// </summary>
    public override void ResetDbType()
    {
        DbType = DbType.Object;
    }
}
