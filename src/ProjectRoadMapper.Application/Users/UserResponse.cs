namespace ProjectRoadMapper.Application.Users;

/// <summary>
/// Public profile information for a user.
/// </summary>
/// <param name="Id">The user's unique identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="CreatedAtUtc">The UTC time at which the account was created.</param>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAtUtc);
