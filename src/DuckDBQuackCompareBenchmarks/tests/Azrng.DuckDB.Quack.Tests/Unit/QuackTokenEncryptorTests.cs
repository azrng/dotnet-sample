using System.Security.Cryptography;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackTokenEncryptorTests 的单元测试
/// </summary>
public class QuackTokenEncryptorTests
{
    /// <summary>
    /// EncryptDecrypt RoundTrips
    /// </summary>
    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var token = "sample-token-for-round-trip";

        var encrypted = QuackTokenEncryptor.Encrypt(token, key);
        var decrypted = QuackTokenEncryptor.Decrypt(encrypted, key);

        Assert.StartsWith("ENC:", encrypted);
        Assert.NotEqual(token, encrypted);
        Assert.Equal(token, decrypted);
    }

    /// <summary>
    /// Encrypt SameTokenTwice ProducesDifferentCiphertext
    /// </summary>
    [Fact]
    public void Encrypt_SameTokenTwice_ProducesDifferentCiphertext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var token = "reproducible-token";

        var first = QuackTokenEncryptor.Encrypt(token, key);
        var second = QuackTokenEncryptor.Encrypt(token, key);

        Assert.NotEqual(first, second);
        Assert.Equal(token, QuackTokenEncryptor.Decrypt(first, key));
        Assert.Equal(token, QuackTokenEncryptor.Decrypt(second, key));
    }

    /// <summary>
    /// Decrypt TamperedCiphertext 抛出
    /// </summary>
    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = QuackTokenEncryptor.Encrypt("secret", key);

        var payload = Convert.FromBase64String(encrypted["ENC:".Length..]);
        payload[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            QuackTokenEncryptor.Decrypt("ENC:" + Convert.ToBase64String(payload), key));
    }

    /// <summary>
    /// Decrypt TruncatedPayload 抛出
    /// </summary>
    [Fact]
    public void Decrypt_TruncatedPayload_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var truncated = "ENC:" + Convert.ToBase64String(new byte[] { 1, 2, 3 });

        Assert.ThrowsAny<CryptographicException>(() => QuackTokenEncryptor.Decrypt(truncated, key));
    }

    /// <summary>
    /// Decrypt DifferentKey 抛出
    /// </summary>
    [Fact]
    public void Decrypt_DifferentKey_Throws()
    {
        var encryptKey = RandomNumberGenerator.GetBytes(32);
        var decryptKey = RandomNumberGenerator.GetBytes(32);
        var encrypted = QuackTokenEncryptor.Encrypt("secret", encryptKey);

        Assert.ThrowsAny<CryptographicException>(() => QuackTokenEncryptor.Decrypt(encrypted, decryptKey));
    }

    /// <summary>
    /// 是否Encrypted 返回TrueForEncrypted
    /// </summary>
    [Fact]
    public void IsEncrypted_ReturnsTrueForEncrypted()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = QuackTokenEncryptor.Encrypt("test", key);

        Assert.True(QuackTokenEncryptor.IsEncrypted(encrypted));
    }

    /// <summary>
    /// 是否Encrypted 返回FalseForPlaintext
    /// </summary>
    [Fact]
    public void IsEncrypted_ReturnsFalseForPlaintext()
    {
        Assert.False(QuackTokenEncryptor.IsEncrypted("plaintext"));
        Assert.False(QuackTokenEncryptor.IsEncrypted(null));
        Assert.False(QuackTokenEncryptor.IsEncrypted(""));
    }

    /// <summary>
    /// Decrypt Plaintext 返回As是否
    /// </summary>
    [Fact]
    public void Decrypt_Plaintext_ReturnsAsIs()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var result = QuackTokenEncryptor.Decrypt("plaintext", key);

        Assert.Equal("plaintext", result);
    }

    /// <summary>
    /// Encrypt Empty 返回Empty
    /// </summary>
    [Fact]
    public void Encrypt_Empty_ReturnsEmpty()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Equal("", QuackTokenEncryptor.Encrypt("", key));
    }

    /// <summary>
    /// Encrypt Null 返回Empty
    /// </summary>
    [Fact]
    public void Encrypt_Null_ReturnsEmpty()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Equal("", QuackTokenEncryptor.Encrypt(null, key));
    }

    /// <summary>
    /// Decrypt NullOrEmpty 返回Empty
    /// </summary>
    [Fact]
    public void Decrypt_NullOrEmpty_ReturnsEmpty()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Equal("", QuackTokenEncryptor.Decrypt(null, key));
        Assert.Equal("", QuackTokenEncryptor.Decrypt("", key));
    }

    /// <summary>
    /// Encrypt ShortKey DerivesViaSha256
    /// </summary>
    [Fact]
    public void Encrypt_ShortKey_DerivesViaSha256()
    {
        var shortKey = RandomNumberGenerator.GetBytes(16);
        var token = "round-trip";

        var encrypted = QuackTokenEncryptor.Encrypt(token, shortKey);

        Assert.Equal(token, QuackTokenEncryptor.Decrypt(encrypted, shortKey));
    }
}
