using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ProjectRoadMapper.Application.Abstractions.Authentication;
using ProjectRoadMapper.Application.Authentication;

namespace ProjectRoadMapper.Infrastructure.Authentication;

internal sealed class RefreshTokenService(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    private const int TokenSizeInBytes = 64;

    public RefreshTokenValue Generate()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(TokenSizeInBytes);

        try
        {
            var token = Convert.ToBase64String(tokenBytes);
            var createdAtUtc = timeProvider.GetUtcNow();
            var expiresAtUtc = createdAtUtc.AddDays(options.Value.RefreshTokenExpirationDays);

            return new RefreshTokenValue(
                token,
                Hash(token),
                createdAtUtc,
                expiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var tokenBytes = Encoding.UTF8.GetBytes(token);

        try
        {
            var hashBytes = SHA256.HashData(tokenBytes);
            return Convert.ToBase64String(hashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }
}
