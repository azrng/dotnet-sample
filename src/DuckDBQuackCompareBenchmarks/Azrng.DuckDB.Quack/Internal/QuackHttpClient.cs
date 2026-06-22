#pragma warning disable CA2000 // HttpClientHandler is disposed in catch block if constructor fails

using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;

namespace Azrng.DuckDB.Quack.Internal;

/// <summary>
/// DuckDB HTTP API 客户端，负责通过 HTTP 协议与 DuckDB 服务端进行通信。
/// </summary>
internal sealed class QuackHttpClient : IDisposable
{
    /// <summary>
    /// 进程级共享 HttpClient（含单一连接池）。未注入 HttpClient、无 SSL 定制、且使用默认超时时，
    /// 所有连接共享它，使并发查询复用 keep-alive TCP 连接，而非各自建/拆 socket —— 后者会在高并发下
    /// 耗尽 OS 临时端口（即 64 并发基准的端口耗尽 10048）。HttpClient 天生线程安全，专为共享而设计。
    /// 生命周期跟随进程；单个连接的 Dispose 不会释放它。超时在构造时一次性设为默认 30s
    /// （HttpClient 的 Timeout 在首次请求后不可再修改，故共享客户端不参与运行期超时调整）。
    /// </summary>
    private static readonly HttpClient s_sharedHttp = new(new HttpClientHandler
    {
        MaxConnectionsPerServer = int.MaxValue,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// 初始化 QuackHttpClient 实例。
    /// </summary>
    /// <param name="httpClient">可选的外部 HttpClient 实例；为 null 时按 SSL/超时配置决定共享或独立。</param>
    /// <param name="timeout">请求超时时间；为 null（默认 30s）时启用共享连接池；显式传值则用独立实例。</param>
    /// <param name="sslOptions">SSL/TLS 配置选项；非 null 时必须使用独立 handler（无法共享连接池）。</param>
    public QuackHttpClient(HttpClient? httpClient = null, TimeSpan? timeout = null, SslOptions? sslOptions = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);

        if (httpClient != null)
        {
            // 调用方拥有该 HttpClient（如自定义工厂）；本实例不释放它。
            _httpClient = httpClient;
            _ownsClient = false;
        }
        else if (sslOptions != null || timeout != null)
        {
            // SSL 定制或自定义超时需要专属客户端（证书回调/超时与共享池不兼容），故走独立实例。
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = int.MaxValue,
            };

            if (sslOptions != null)
            {
                if (sslOptions.DisableCertificateValidation)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }
                else if (sslOptions.CustomCaCertificate != null)
                {
                    var caCert = sslOptions.CustomCaCertificate;
                    handler.ServerCertificateCustomValidationCallback = (_, _, chain, _) =>
                    {
                        if (chain == null) return false;
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(caCert);
                        return chain.Build(chain.ChainElements[0].Certificate);
                    };
                }
                // else: leave default handler behavior — OS-trusted CAs validate the server certificate.
            }

            try
            {
                _httpClient = new HttpClient(handler);
                _httpClient.Timeout = _timeout;
            }
            catch
            {
                handler.Dispose();
                throw;
            }
            _ownsClient = true;
        }
        else
        {
            // 默认路径（无注入、无 SSL、默认超时）：共享进程级连接池。各连接的 Dispose 不释放共享客户端。
            _httpClient = s_sharedHttp;
            _ownsClient = false;
        }
    }

    /// <summary>
    /// 向指定端点发送 HTTP POST 请求并返回响应的原始字节。
    /// </summary>
    /// <param name="endpoint">请求的目标 URI。</param>
    /// <param name="body">请求体的字节数组。</param>
    /// <param name="length">从请求体缓冲区发送的字节数。</param>
    /// <param name="cancellationToken">用于取消异步操作的令牌。</param>
    /// <returns>响应体的字节数组。</returns>
    public async Task<byte[]> PostAsync(Uri endpoint, byte[] body, int length, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(body, 0, length);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var errorBody = errorBytes.Length > 0 && errorBytes.All(b => b >= 0x20 && b < 0x7F || b == '\n' || b == '\r' || b == '\t')
                ? System.Text.Encoding.UTF8.GetString(errorBytes)
                : $"[binary {errorBytes.Length} bytes] {Convert.ToHexString(errorBytes)}";
            throw new QuackProtocolException($"HTTP {response.StatusCode}: {errorBody}", (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// 释放 HttpClient 资源。仅在客户端由本实例创建时才执行释放。
    /// </summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// SSL/TLS configuration options.
/// </summary>
public sealed class SslOptions
{
    /// <summary>
    /// Gets or sets whether to disable certificate validation.
    /// Default is false — server certificates are validated against the OS trust store.
    /// </summary>
    public bool DisableCertificateValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets a custom CA certificate for validation.
    /// </summary>
    public X509Certificate2? CustomCaCertificate { get; set; }
}
