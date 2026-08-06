using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Domain.Authentication;
using ProjectRoadMapper.Domain.Users;

namespace ProjectRoadMapper.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<User> Users => Set<User>();

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(
                "A concurrent persistence operation modified the same data.",
                exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new UniqueConstraintViolationException(
                "A unique database constraint was violated.",
                exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
