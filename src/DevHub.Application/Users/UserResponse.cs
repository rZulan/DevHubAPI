namespace DevHub.Application.Users;

/// <summary>
/// Public profile information for a user.
/// </summary>
/// <param name="Id">The user's unique identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="Username">The user's public username.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="AvatarUrl">The user's optional avatar URL.</param>
/// <param name="ConnectedAccounts">The external accounts linked to the user.</param>
/// <param name="CreatedAtUtc">The UTC time at which the account was created.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string Username,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    IReadOnlyCollection<ConnectedAccountResponse> ConnectedAccounts,
    DateTimeOffset CreatedAtUtc);

public sealed record ConnectedAccountResponse(
    string Provider,
    string? ProviderUsername);
