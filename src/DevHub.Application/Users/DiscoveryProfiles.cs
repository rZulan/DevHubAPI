using DevHub.Application.Abstractions.Persistence;
using MediatR;

namespace DevHub.Application.Users;

public sealed record DiscoveryProfile(
    Guid Id,
    string Username,
    string FirstName,
    string LastName,
    string? AvatarUrl);

public sealed record DiscoveryProfilePage(IReadOnlyList<DiscoveryProfile> Items, string? NextCursor);

public sealed record ListDiscoveryProfilesQuery(Guid ViewerId, string? After) : IRequest<DiscoveryProfilePage>;

public sealed record GetDiscoveryProfileQuery(string Username) : IRequest<DiscoveryProfile?>;

internal sealed class DiscoveryProfileQueries(IUserRepository users)
    : IRequestHandler<ListDiscoveryProfilesQuery, DiscoveryProfilePage>,
        IRequestHandler<GetDiscoveryProfileQuery, DiscoveryProfile?>
{
    private const int PageSize = 100;

    public async Task<DiscoveryProfilePage> Handle(ListDiscoveryProfilesQuery request, CancellationToken cancellationToken)
    {
        var profiles = await users.ListDiscoveryProfilesAsync(
            request.ViewerId, request.After, PageSize + 1, cancellationToken);

        return profiles.Count > PageSize
            ? new DiscoveryProfilePage(profiles.Take(PageSize).ToArray(), profiles[PageSize - 1].Username)
            : new DiscoveryProfilePage(profiles, null);
    }

    public Task<DiscoveryProfile?> Handle(GetDiscoveryProfileQuery request, CancellationToken cancellationToken) =>
        users.GetDiscoveryProfileAsync(request.Username, cancellationToken);
}
