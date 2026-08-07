using DevHub.Domain.Users;

namespace DevHub.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(
        string username,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default);

    void Add(User user);
}
