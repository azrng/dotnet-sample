using System.Net.Http;
using System.Reflection;
using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackHttpClientSslTests 的单元测试
/// </summary>
public class QuackHttpClientSslTests
{
    private static HttpClient? ExtractHttpClient(QuackHttpClient client)
    {
        var field = typeof(QuackHttpClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        return (HttpClient?)field?.GetValue(client);
    }

    private static HttpClientHandler? ExtractHandler(HttpClient client)
    {
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);
        return handlerField?.GetValue(client) as HttpClientHandler;
    }

    /// <summary>
    /// Constructor NoSslOptions DoesNotInstallTrustAnythingCallback
    /// </summary>
    [Fact]
    public void Constructor_NoSslOptions_DoesNotInstallTrustAnythingCallback()
    {
        using var client = new QuackHttpClient();
        var http = ExtractHttpClient(client);
        Assert.NotNull(http);
        var handler = ExtractHandler(http!);

        // QuackHttpClient owns the handler only when no HttpClient was injected.
        if (handler is not null)
        {
            Assert.Null(handler.ServerCertificateCustomValidationCallback);
        }
    }

    /// <summary>
    /// Constructor DisableCertificateValidationTrue InstallsAlwaysTrueCallback
    /// </summary>
    [Fact]
    public void Constructor_DisableCertificateValidationTrue_InstallsAlwaysTrueCallback()
    {
        using var client = new QuackHttpClient(sslOptions: new SslOptions { DisableCertificateValidation = true });
        var http = ExtractHttpClient(client);
        Assert.NotNull(http);
        var handler = ExtractHandler(http!);
        Assert.NotNull(handler);

        var callback = handler!.ServerCertificateCustomValidationCallback;
        Assert.NotNull(callback);

        // Invoke with garbage and ensure it returns true — that's the dangerous behavior we asked for.
        var result = callback!.Invoke(null!, null!, null!, default);
        Assert.True(result);
    }

    /// <summary>
    /// Constructor DisableCertificateValidationFalse NoAlwaysTrueCallback
    /// </summary>
    [Fact]
    public void Constructor_DisableCertificateValidationFalse_NoAlwaysTrueCallback()
    {
        using var client = new QuackHttpClient(sslOptions: new SslOptions { DisableCertificateValidation = false });
        var http = ExtractHttpClient(client);
        Assert.NotNull(http);
        var handler = ExtractHandler(http!);

        if (handler is not null)
        {
            Assert.Null(handler.ServerCertificateCustomValidationCallback);
        }
    }
}
