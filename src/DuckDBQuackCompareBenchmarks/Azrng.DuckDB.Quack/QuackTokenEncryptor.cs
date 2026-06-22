using System.Security.Cryptography;
using System.Text;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// Provides encryption/decryption for connection string tokens using AES-GCM with authenticated encryption.
/// </summary>
public static class QuackTokenEncryptor
{
    private const string Prefix = "ENC:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Encrypts a token using AES-GCM.
    /// </summary>
    /// <param name="token">待加密的令牌字符串，为 null 或空时直接返回空字符串。</param>
    /// <param name="key">加密密钥，长度不足 32 字节时会通过 SHA256 派生。</param>
    /// <returns>以 "ENC:" 前缀开头的 Base64 编码密文。</returns>
    public static string Encrypt(string? token, byte[] key)
    {
        if (string.IsNullOrEmpty(token))
            return token ?? "";

        if (key is null)
            throw new ArgumentNullException(nameof(key));

        var keyMaterial = DeriveKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var cipher = new byte[tokenBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(keyMaterial, TagSize);
        aes.Encrypt(nonce, tokenBytes, cipher, tag);

        var result = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + cipher.Length, TagSize);

        return Prefix + Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts an encrypted token. Strings without the ENC: prefix are returned unchanged.
    /// </summary>
    /// <param name="encryptedToken">待解密的密文字符串，不以 "ENC:" 开头时原样返回。</param>
    /// <param name="key">解密密钥，必须与加密时使用的密钥一致。</param>
    /// <returns>解密后的明文字符串。</returns>
    /// <exception cref="CryptographicException">Thrown when the payload is truncated or authentication fails.</exception>
    public static string Decrypt(string? encryptedToken, byte[] key)
    {
        if (string.IsNullOrEmpty(encryptedToken))
            return encryptedToken ?? "";

        if (!encryptedToken.StartsWith(Prefix, StringComparison.Ordinal))
            return encryptedToken;

        if (key is null)
            throw new ArgumentNullException(nameof(key));

        var data = Convert.FromBase64String(encryptedToken[Prefix.Length..]);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted payload is too short.");

        var keyMaterial = DeriveKey(key);
        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(data.Length - TagSize, TagSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(keyMaterial, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Checks if a token is encrypted.
    /// </summary>
    /// <param name="token">待检查的令牌字符串。</param>
    /// <returns>如果令牌以 "ENC:" 前缀开头则返回 true，否则返回 false。</returns>
    public static bool IsEncrypted(string? token)
    {
        return !string.IsNullOrEmpty(token) && token.StartsWith(Prefix, StringComparison.Ordinal);
    }

    private static byte[] DeriveKey(byte[] key)
    {
        if (key.Length == KeySize)
            return key;

        return SHA256.HashData(key);
    }
}
