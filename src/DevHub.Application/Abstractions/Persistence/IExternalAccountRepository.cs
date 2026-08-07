using DevHub.Domain.Authentication;

namespace DevHub.Application.Abstractions.Persistence;

public interface IExternalAccountRepository
{
    Task<ExternalAccount?> GetByProviderIdentityAsync(
        string provider,
        string providerUserId,
        CancellationToken cancellationToken = default);

    Task<ExternalAccount?> GetByUserAndProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default);

    void Add(ExternalAccount externalAccount);

    void Remove(ExternalAccount externalAccount);
}
