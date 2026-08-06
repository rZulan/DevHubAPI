using ProjectRoadMapper.Domain.Authentication;

namespace ProjectRoadMapper.Application.Abstractions.Persistence;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default);

    void Add(RefreshToken refreshToken);
}
