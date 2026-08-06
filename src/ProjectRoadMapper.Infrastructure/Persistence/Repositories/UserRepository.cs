using Microsoft.EntityFrameworkCore;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Domain.Users;

namespace ProjectRoadMapper.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = User.NormalizeEmail(email);

        return dbContext.Users
            .SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    public void Add(User user) => dbContext.Users.Add(user);
}
