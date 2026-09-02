using DevHub.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationRoleConfiguration : IEntityTypeConfiguration<OrganizationRole>
{
    public void Configure(EntityTypeBuilder<OrganizationRole> builder)
    {
        builder.ToTable("OrganizationRoles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Name).HasMaxLength(100).IsRequired();
        builder.Property(role => role.Color).HasMaxLength(20).IsRequired();
        builder.Property(role => role.Position).IsRequired();
        builder.Property(role => role.IsOwnerRole).IsRequired();
        builder.Property(role => role.IsDefaultRole).IsRequired();
        builder.Property(role => role.PermissionsValue).HasMaxLength(1000).IsRequired();
        builder.Ignore(role => role.Permissions);
        builder.HasIndex(role => new { role.OrganizationId, role.Position });
        builder.HasIndex(role => new { role.OrganizationId, role.Name }).IsUnique();
        builder.HasOne(role => role.Organization)
            .WithMany(organization => organization.Roles)
            .HasForeignKey(role => role.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
