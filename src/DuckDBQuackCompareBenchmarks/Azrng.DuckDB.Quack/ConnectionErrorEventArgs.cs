namespace Azrng.DuckDB.Quack;

/// <summary>
/// 连接错误事件的参数。
/// </summary>
public sealed class ConnectionErrorEventArgs : EventArgs
{
    /// <summary>获取发生的异常。</summary>
    public Exception Exception { get; }

    /// <summary>
    /// 初始化 <see cref="ConnectionErrorEventArgs"/> 的新实例。
    /// </summary>
    /// <param name="exception">发生的异常。</param>
    public ConnectionErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }
}
