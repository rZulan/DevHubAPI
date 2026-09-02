using DevHub.Domain.Users;

namespace DevHub.Application.Users;

internal static class UserMappings
{
    public static UserResponse ToUserResponse(this User user)
    {
        var avatarUrl = user.AvatarUrl ?? user.ExternalAccounts
            .Select(account => account.AvatarUrl)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new(
            user.Id,
            user.Email,
            user.Username,
            user.FirstName,
            user.LastName,
            user.DateOfBirth,
            avatarUrl,
            user.ExternalAccounts
                .OrderBy(account => account.Provider)
                .Select(account => new ConnectedAccountResponse(
                    account.Provider,
                    account.ProviderUsername))
                .ToArray(),
            user.CreatedAtUtc);
    }
}
