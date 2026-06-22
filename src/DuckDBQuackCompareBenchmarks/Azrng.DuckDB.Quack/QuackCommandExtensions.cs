using System.Data.Common;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack 命令的扩展方法，简化参数操作。
/// </summary>
public static class QuackCommandExtensions
{
    /// <summary>
    /// 向命令中添加一个参数。
    /// </summary>
    /// <param name="command">数据库命令对象。</param>
    /// <param name="name">参数名称（如 "@id"）。</param>
    /// <param name="value">参数值。</param>
    /// <returns>添加的参数对象，可用于进一步配置。</returns>
    public static DbParameter AddParam(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// 向命令中添加一个参数，并指定数据库类型。
    /// </summary>
    /// <param name="command">数据库命令对象。</param>
    /// <param name="name">参数名称（如 "@id"）。</param>
    /// <param name="value">参数值。</param>
    /// <param name="dbType">参数的数据库类型。</param>
    /// <returns>添加的参数对象，可用于进一步配置。</returns>
    public static DbParameter AddParam(this DbCommand command, string name, object value, System.Data.DbType dbType)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        parameter.DbType = dbType;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// 向命令中添加一个空值参数。
    /// </summary>
    /// <param name="command">数据库命令对象。</param>
    /// <param name="name">参数名称（如 "@id"）。</param>
    /// <returns>添加的参数对象，可用于进一步配置。</returns>
    public static DbParameter AddNullParam(this DbCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
