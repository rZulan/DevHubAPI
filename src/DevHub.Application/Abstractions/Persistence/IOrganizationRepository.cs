using DevHub.Domain.Organizations;
using DevHub.Domain.Users;

namespace DevHub.Application.Abstractions.Persistence;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Organization>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task<OrganizationInvite?> GetInviteByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    void Add(Organization organization);
    void AddInvite(OrganizationInvite invite);
    void Remove(Organization organization);
}
