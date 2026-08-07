using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Teams;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class TeamRepository(ApplicationDbContext dbContext) : ITeamRepository
{
    public Task<Team?> GetByIdAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Teams
            .Include(team => team.Members)
            .SingleOrDefaultAsync(
                team => team.Id == id && team.OrganizationId == organizationId,
                cancellationToken);

    public async Task<IReadOnlyList<Team>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Teams
            .AsNoTracking()
            .Include(team => team.Members)
            .Where(team => team.OrganizationId == organizationId)
            .OrderBy(team => team.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListMembersAsync(
        Guid teamId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => dbContext.Set<TeamMember>().Any(
                member => member.TeamId == teamId && member.UserId == user.Id))
            .OrderBy(user => user.FirstName)
            .ThenBy(user => user.LastName)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        Guid organizationId,
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = Team.NormalizeName(name);
        return dbContext.Teams.AnyAsync(
            team => team.OrganizationId == organizationId &&
                    team.NormalizedName == normalizedName &&
                    (!excludingId.HasValue || team.Id != excludingId.Value),
            cancellationToken);
    }

    public void Add(Team team) => dbContext.Teams.Add(team);
    public void Remove(Team team) => dbContext.Teams.Remove(team);
}
