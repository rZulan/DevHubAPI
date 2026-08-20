using DevHub.Domain.Projects;

namespace DevHub.Application.Abstractions.Persistence;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListVisibleAsync(
        Guid organizationId,
        Guid userId,
        bool includeAll,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        Guid organizationId,
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default);

    void Add(Project project);
    void Remove(Project project);
}
