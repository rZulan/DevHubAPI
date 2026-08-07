using DevHub.Domain.Teams;
using DevHub.Domain.Users;

namespace DevHub.Application.Abstractions.Persistence;

public interface ITeamRepository
{
    Task<Team?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Team>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludingId = null, CancellationToken cancellationToken = default);
    void Add(Team team);
    void Remove(Team team);
}
