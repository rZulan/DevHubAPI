using Microsoft.EntityFrameworkCore;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Domain.Authentication;

namespace ProjectRoadMapper.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(ApplicationDbContext dbContext)
    : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens.SingleOrDefaultAsync(
            refreshToken => refreshToken.TokenHash == tokenHash,
            cancellationToken);

    public async Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await dbContext.RefreshTokens
            .Where(refreshToken =>
                refreshToken.FamilyId == familyId &&
                refreshToken.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    refreshToken => refreshToken.RevokedAtUtc,
                    revokedAtUtc),
                cancellationToken);
    }

    public void Add(RefreshToken refreshToken) =>
        dbContext.RefreshTokens.Add(refreshToken);
}
