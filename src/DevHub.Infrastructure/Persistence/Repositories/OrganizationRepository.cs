using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class OrganizationRepository(ApplicationDbContext dbContext)
    : IOrganizationRepository
{
    public Task<Organization?> GetDashboardForMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Organizations.AsNoTracking().SingleOrDefaultAsync(
            organization => organization.Id == organizationId && organization.Members.Any(member => member.UserId == userId), cancellationToken);

    public Task<Organization?> GetMembershipForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Organizations.AsNoTracking().AsSplitQuery()
            .Include(organization => organization.Members).ThenInclude(member => member.RoleAssignments)
            .SingleOrDefaultAsync(organization => organization.Id == organizationId &&
                organization.Members.Any(member => member.UserId == userId), cancellationToken);

    public Task<bool> IsMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Set<OrganizationMember>().AnyAsync(
            member => member.OrganizationId == organizationId && member.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<DevHub.Application.Organizations.OrganizationResponse>> ListSummariesForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Members.Any(member => member.UserId == userId))
            .OrderBy(organization => organization.Name)
            .Select(organization => new DevHub.Application.Organizations.OrganizationResponse(
                organization.Id, organization.OwnerUserId, organization.Name, organization.Description,
                organization.CreatedAtUtc, organization.UpdatedAtUtc, organization.Members.Count, organization.Teams.Count))
            .ToListAsync(cancellationToken);

    public Task<Organization?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Organizations
            .AsSplitQuery()
            .Include(organization => organization.Members)
                .ThenInclude(member => member.RoleAssignments)
            .Include(organization => organization.Roles)
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
                .ThenInclude(member => member.RoleAssignments)
            .Include(organization => organization.Roles)
            .Include(organization => organization.Teams)
            .Where(organization => organization.Members.Any(member => member.UserId == userId))
            .OrderBy(organization => organization.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .Include(user => user.ExternalAccounts)
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

    public Task<OrganizationInvite?> GetInviteByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        dbContext.OrganizationInvites
            .SingleOrDefaultAsync(invite => invite.TokenHash == tokenHash, cancellationToken);

    public void Add(Organization organization) => dbContext.Organizations.Add(organization);
    public void AddInvite(OrganizationInvite invite) => dbContext.OrganizationInvites.Add(invite);
    public void Remove(Organization organization) => dbContext.Organizations.Remove(organization);
}
