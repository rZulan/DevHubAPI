using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.Commands.RegisterUser;

/// <summary>
/// Information required to create a user account.
/// </summary>
public sealed record RegisterUserCommand : IRequest<Result<AuthenticationResponse>>
{
    [JsonConstructor]
    public RegisterUserCommand(
        string email,
        string firstName,
        string lastName,
        string password)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Password = password;
    }

    /// <summary>
    /// The user's email address. Email matching is case-insensitive.
    /// </summary>
    /// <example>alex@example.com</example>
    [Required(ErrorMessage = "Email is required.")]
    [StringLength(320, ErrorMessage = "Email must not exceed 320 characters.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; init; }

    /// <summary>
    /// The user's given name.
    /// </summary>
    /// <example>Alex</example>
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name must not exceed 100 characters.")]
    public string FirstName { get; init; }

    /// <summary>
    /// The user's family name.
    /// </summary>
    /// <example>Rivera</example>
    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name must not exceed 100 characters.")]
    public string LastName { get; init; }

    /// <summary>
    /// The account password. It must contain between 8 and 128 characters.
    /// </summary>
    /// <example>CorrectHorseBatteryStaple</example>
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(
        128,
        MinimumLength = 8,
        ErrorMessage = "Password must be between 8 and 128 characters.")]
    public string Password { get; init; }
}
