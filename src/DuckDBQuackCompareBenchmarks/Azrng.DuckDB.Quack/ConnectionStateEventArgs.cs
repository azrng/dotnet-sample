using System.Data;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// 连接状态变化事件的参数。
/// </summary>
public sealed class ConnectionStateEventArgs : EventArgs
{
    /// <summary>获取旧的连接状态。</summary>
    public ConnectionState OldState { get; }
    /// <summary>获取新的连接状态。</summary>
    public ConnectionState NewState { get; }

    /// <summary>
    /// 初始化 <see cref="ConnectionStateEventArgs"/> 的新实例。
    /// </summary>
    /// <param name="oldState">旧的连接状态。</param>
    /// <param name="newState">新的连接状态。</param>
    public ConnectionStateEventArgs(ConnectionState oldState, ConnectionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}
