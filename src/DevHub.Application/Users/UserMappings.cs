using DevHub.Domain.Users;

namespace DevHub.Application.Users;

internal static class UserMappings
{
    public static UserResponse ToUserResponse(this User user) =>
        new(
            user.Id,
            user.Email,
            user.Username,
            user.FirstName,
            user.LastName,
            user.DateOfBirth,
            user.AvatarUrl,
            user.ExternalAccounts
                .OrderBy(account => account.Provider)
                .Select(account => new ConnectedAccountResponse(
                    account.Provider,
                    account.ProviderUsername))
                .ToArray(),
            user.CreatedAtUtc);
}
