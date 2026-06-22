namespace Azrng.DuckDB.Quack;

/// <summary>
/// 协议桥的版本信息，包含原生 ABI 版本、DuckDB 版本和 Quack 库版本。
/// </summary>
/// <param name="NativeAbiVersion">原生 ABI 版本号。</param>
/// <param name="DuckDbVersion">DuckDB 引擎版本字符串。</param>
/// <param name="QuackVersion">Quack 库版本字符串。</param>
public sealed record QuackProtocolBridgeVersion(
    int NativeAbiVersion,
    string DuckDbVersion,
    string QuackVersion);
