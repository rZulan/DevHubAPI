using System.Security.Cryptography;
using System.Text;
using DevHub.Application.Chats;
using Microsoft.Extensions.Configuration;

namespace DevHub.Infrastructure.Chats;

internal sealed class AesChatMessageCipher : IChatMessageCipher
{
    private const string CurrentKeyVersion = "v1";
    private readonly byte[] _key;

    public AesChatMessageCipher(IConfiguration configuration)
    {
        var secret = configuration["Chat:EncryptionKey"] ?? configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "Chat encryption requires Chat:EncryptionKey (or the existing Jwt:Key fallback) to be configured.");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes($"DevHub.Chat.{CurrentKeyVersion}:{secret}"));
    }

    public EncryptedChatContent Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherText = new byte[plainBytes.Length];
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plainBytes, cipherText, tag);
        CryptographicOperations.ZeroMemory(plainBytes);
        return new EncryptedChatContent(cipherText, nonce, tag, CurrentKeyVersion);
    }

    public string Decrypt(
        byte[] cipherText,
        byte[] nonce,
        byte[] authenticationTag,
        string keyVersion)
    {
        if (!string.Equals(keyVersion, CurrentKeyVersion, StringComparison.Ordinal))
        {
            throw new CryptographicException($"Unsupported chat encryption key version '{keyVersion}'.");
        }

        var plainBytes = new byte[cipherText.Length];
        try
        {
            using var aes = new AesGcm(_key, authenticationTag.Length);
            aes.Decrypt(nonce, cipherText, authenticationTag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}
