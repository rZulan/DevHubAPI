using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationInviteConfiguration : IEntityTypeConfiguration<OrganizationInvite>
{
    public void Configure(EntityTypeBuilder<OrganizationInvite> builder)
    {
        builder.ToTable("OrganizationInvites");
        builder.HasKey(invite => invite.Id);
        builder.Property(invite => invite.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(invite => invite.TokenHash).IsUnique();
        builder.Property(invite => invite.ExpiresAtUtc).IsRequired();
        builder.Property(invite => invite.CreatedAtUtc).IsRequired();
        builder.HasOne(invite => invite.Organization)
            .WithMany()
            .HasForeignKey(invite => invite.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invite => invite.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
