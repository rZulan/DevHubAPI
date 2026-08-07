using Microsoft.EntityFrameworkCore;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Users;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.ExternalAccounts)
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.ExternalAccounts)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = User.NormalizeEmail(email);

        return dbContext.Users
            .Include(user => user.ExternalAccounts)
            .SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(
        string username,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = User.NormalizeUsername(username);

        return dbContext.Users.AnyAsync(
            user =>
                user.NormalizedUsername == normalizedUsername &&
                (!excludingUserId.HasValue || user.Id != excludingUserId.Value),
            cancellationToken);
    }

    public void Add(User user) => dbContext.Users.Add(user);
}
