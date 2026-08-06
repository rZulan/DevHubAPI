using Microsoft.AspNetCore.Identity;
using DevHub.Application.Abstractions.Authentication;

namespace DevHub.Infrastructure.Authentication;

internal sealed class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly object PasswordOwner = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string password) =>
        _passwordHasher.HashPassword(PasswordOwner, password);

    public bool Verify(string passwordHash, string providedPassword)
    {
        var result = _passwordHasher.VerifyHashedPassword(
            PasswordOwner,
            passwordHash,
            providedPassword);

        return result is PasswordVerificationResult.Success or
            PasswordVerificationResult.SuccessRehashNeeded;
    }
}
