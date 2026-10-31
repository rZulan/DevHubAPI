using System.ComponentModel.DataAnnotations;
using DevHub.Domain.Common;

namespace DevHub.Application.Common;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AvatarImageAttribute : ValidationAttribute
{
    public AvatarImageAttribute() : base("Choose a PNG, JPEG or WebP avatar smaller than 128 KB.") { }
    public override bool IsValid(object? value) => value is null || value is string image && AvatarImage.IsValid(image);
}
