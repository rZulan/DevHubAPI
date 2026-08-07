using Microsoft.EntityFrameworkCore;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Authentication;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class ExternalAccountRepository(ApplicationDbContext dbContext)
    : IExternalAccountRepository
{
    public Task<ExternalAccount?> GetByProviderIdentityAsync(
        string provider,
        string providerUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();

        return dbContext.ExternalAccounts.SingleOrDefaultAsync(
            account =>
                account.Provider == normalizedProvider &&
                account.ProviderUserId == providerUserId,
            cancellationToken);
    }

    public Task<ExternalAccount?> GetByUserAndProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();

        return dbContext.ExternalAccounts.SingleOrDefaultAsync(
            account =>
                account.UserId == userId &&
                account.Provider == normalizedProvider,
            cancellationToken);
    }

    public void Add(ExternalAccount externalAccount) =>
        dbContext.ExternalAccounts.Add(externalAccount);

    public void Remove(ExternalAccount externalAccount) =>
        dbContext.ExternalAccounts.Remove(externalAccount);
}
