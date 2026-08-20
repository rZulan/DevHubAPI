using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Projects;
using DevHub.Domain.Teams;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence.Repositories;

internal sealed class ProjectRepository(ApplicationDbContext dbContext) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Projects.SingleOrDefaultAsync(
            project => project.Id == id && project.OrganizationId == organizationId,
            cancellationToken);

    public async Task<IReadOnlyList<Project>> ListVisibleAsync(
        Guid organizationId,
        Guid userId,
        bool includeAll,
        CancellationToken cancellationToken = default) =>
        await dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.OrganizationId == organizationId &&
                (includeAll || dbContext.Set<TeamMember>().Any(member =>
                    member.TeamId == project.TeamId && member.UserId == userId)))
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        Guid organizationId,
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = Project.NormalizeName(name);
        return dbContext.Projects.AnyAsync(
            project => project.OrganizationId == organizationId &&
                       project.NormalizedName == normalizedName &&
                       (!excludingId.HasValue || project.Id != excludingId.Value),
            cancellationToken);
    }

    public void Add(Project project) => dbContext.Projects.Add(project);
    public void Remove(Project project) => dbContext.Projects.Remove(project);
}
