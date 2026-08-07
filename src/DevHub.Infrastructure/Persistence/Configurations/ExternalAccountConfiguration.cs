using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ExternalAccountConfiguration
    : IEntityTypeConfiguration<ExternalAccount>
{
    public void Configure(EntityTypeBuilder<ExternalAccount> builder)
    {
        builder.ToTable("ExternalAccounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Provider)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(account => account.ProviderUserId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(account => account.ProviderUsername)
            .HasMaxLength(255);

        builder.Property(account => account.ProviderEmail)
            .HasMaxLength(320);

        builder.Property(account => account.AvatarUrl)
            .HasMaxLength(2048);

        builder.Property(account => account.CreatedAtUtc)
            .IsRequired();

        builder.Property(account => account.UpdatedAtUtc);

        builder.HasIndex(account => new
        {
            account.Provider,
            account.ProviderUserId
        })
            .IsUnique();

        builder.HasIndex(account => new
        {
            account.UserId,
            account.Provider
        })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany(user => user.ExternalAccounts)
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
