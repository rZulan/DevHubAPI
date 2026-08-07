namespace DevHub.Application.Authentication.ExternalAuthentication;

public sealed record ExternalIdentity(
    string Provider,
    string ProviderUserId,
    string Email,
    string? ProviderUsername,
    string FirstName,
    string LastName,
    string? AvatarUrl);
