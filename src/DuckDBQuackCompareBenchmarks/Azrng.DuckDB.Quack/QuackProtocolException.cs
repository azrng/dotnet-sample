namespace Azrng.DuckDB.Quack;

/// <summary>
/// Quack 协议异常，表示 DuckDB 通信过程中发生的协议层错误。
/// </summary>
public class QuackProtocolException : Exception
{
    /// <summary>
    /// 使用指定的错误消息初始化 <see cref="QuackProtocolException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    public QuackProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用指定的错误消息和内部异常初始化 <see cref="QuackProtocolException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="innerException">导致当前异常的内部异常。</param>
    public QuackProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// 使用指定的错误消息和 HTTP 状态码初始化 <see cref="QuackProtocolException"/> 类的新实例。
    /// </summary>
    /// <param name="message">描述错误的消息。</param>
    /// <param name="statusCode">来自服务器响应的 HTTP 状态码，如果不可用则为 null。</param>
    public QuackProtocolException(string message, int? statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// HTTP status code from the server response, if available. Null for non-HTTP failures.
    /// </summary>
    public int? StatusCode { get; }
}
