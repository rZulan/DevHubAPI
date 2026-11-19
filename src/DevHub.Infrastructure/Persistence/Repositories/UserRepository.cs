using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Users;
using DevHub.Domain.Users;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    private static readonly Expression<Func<User, DiscoveryProfile>> DiscoveryProfileProjection = user => new(
        user.Id,
        user.Username,
        user.FirstName,
        user.LastName,
        user.AvatarUrl ?? user.ExternalAccounts
            .Where(account => account.AvatarUrl != null && account.AvatarUrl != "")
            .OrderBy(account => account.Provider)
            .Select(account => account.AvatarUrl)
            .FirstOrDefault());

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

    public async Task<IReadOnlyList<DiscoveryProfile>> ListDiscoveryProfilesAsync(
        Guid viewerId, string? after, int limit, CancellationToken cancellationToken)
    {
        var profiles = dbContext.Users.Where(user => user.Id != viewerId);
        if (!string.IsNullOrWhiteSpace(after))
        {
            var cursor = User.NormalizeUsername(after);
            profiles = profiles.Where(user => string.Compare(user.NormalizedUsername, cursor) > 0);
        }

        return await profiles
            .OrderBy(user => user.NormalizedUsername)
            .Take(limit)
            .Select(DiscoveryProfileProjection)
            .ToListAsync(cancellationToken);
    }

    public Task<DiscoveryProfile?> GetDiscoveryProfileAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Task.FromResult<DiscoveryProfile?>(null);

        var normalizedUsername = User.NormalizeUsername(username);
        return dbContext.Users
            .Where(user => user.NormalizedUsername == normalizedUsername)
            .Select(DiscoveryProfileProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(User user) => dbContext.Users.Add(user);
}
