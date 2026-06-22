using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// SslOptionsTests 的单元测试
/// </summary>
public class SslOptionsTests
{
    /// <summary>
    /// SslOptions DefaultValues
    /// </summary>
    [Fact]
    public void SslOptions_DefaultValues()
    {
        var options = new SslOptions();

        Assert.False(options.DisableCertificateValidation);
        Assert.Null(options.CustomCaCertificate);
    }

    /// <summary>
    /// SslOptions 可以SetDisableCertificateValidation
    /// </summary>
    [Fact]
    public void SslOptions_CanSetDisableCertificateValidation()
    {
        var options = new SslOptions
        {
            DisableCertificateValidation = true
        };

        Assert.True(options.DisableCertificateValidation);
    }
}
