using DevHub.Application.Common;

namespace DevHub.Application.Authentication;

internal static class AuthenticationErrors
{
    public static Error InvalidRegistration(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(
            "Authentication.InvalidRegistration",
            "One or more registration fields are invalid.",
            ErrorType.Validation,
            validationErrors);

    public static readonly Error EmailAlreadyExists = new(
        "Authentication.EmailAlreadyExists",
        "A user with this email address already exists.",
        ErrorType.Conflict);

    public static readonly Error UsernameAlreadyExists = new(
        "Authentication.UsernameAlreadyExists",
        "A user with this username already exists.",
        ErrorType.Conflict);

    public static readonly Error AccountAlreadyExists = new(
        "Authentication.AccountAlreadyExists",
        "An account with this email address or username already exists.",
        ErrorType.Conflict);

    public static readonly Error ExistingAccountMustBeLinked = new(
        "Authentication.ExistingAccountMustBeLinked",
        "An account already uses this email address. Sign in with its existing method, then link this provider from account settings.",
        ErrorType.Conflict);

    public static readonly Error ExternalAccountAlreadyLinked = new(
        "Authentication.ExternalAccountAlreadyLinked",
        "This external identity is already linked to an account.",
        ErrorType.Conflict);

    public static readonly Error ProviderAlreadyLinked = new(
        "Authentication.ProviderAlreadyLinked",
        "This account already has a connection for that provider.",
        ErrorType.Conflict);

    public static readonly Error FinalSignInMethod = new(
        "Authentication.FinalSignInMethod",
        "Set a password or connect another provider before disconnecting this account.",
        ErrorType.Conflict);

    public static readonly Error InvalidCredentials = new(
        "Authentication.InvalidCredentials",
        "The email address or password is invalid.",
        ErrorType.Unauthorized);

    public static readonly Error InvalidRefreshToken = new(
        "Authentication.InvalidRefreshToken",
        "The refresh token is invalid or has expired.",
        ErrorType.Unauthorized);
}
