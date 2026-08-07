using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class OrganizationRepository(ApplicationDbContext dbContext)
    : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Organizations
            .AsSplitQuery()
            .Include(organization => organization.Members)
            .Include(organization => organization.Teams)
                .ThenInclude(team => team.Members)
            .SingleOrDefaultAsync(organization => organization.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Organization>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(organization => organization.Members)
            .Include(organization => organization.Teams)
            .Where(organization => organization.Members.Any(member => member.UserId == userId))
            .OrderBy(organization => organization.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => dbContext.Set<OrganizationMember>().Any(
                member => member.OrganizationId == organizationId && member.UserId == user.Id))
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = Organization.NormalizeName(name);
        return dbContext.Organizations.AnyAsync(
            organization => organization.NormalizedName == normalizedName &&
                            (!excludingId.HasValue || organization.Id != excludingId.Value),
            cancellationToken);
    }

    public void Add(Organization organization) => dbContext.Organizations.Add(organization);
    public void Remove(Organization organization) => dbContext.Organizations.Remove(organization);
}
