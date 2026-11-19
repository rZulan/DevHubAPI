using DevHub.Application.Users;
using DevHub.Domain.Users;

namespace DevHub.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveryProfile>> ListDiscoveryProfilesAsync(
        Guid viewerId, string? after, int limit, CancellationToken cancellationToken);

    Task<DiscoveryProfile?> GetDiscoveryProfileAsync(string username, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(
        string username,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default);

    void Add(User user);
}
