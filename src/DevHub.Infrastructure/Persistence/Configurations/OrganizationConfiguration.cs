using DevHub.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Name).HasMaxLength(150).IsRequired();
        builder.Property(organization => organization.NormalizedName).HasMaxLength(150).IsRequired();
        builder.HasIndex(organization => organization.NormalizedName).IsUnique();
        builder.Property(organization => organization.Description).HasMaxLength(1000);
        builder.Property(organization => organization.CreatedAtUtc).IsRequired();
        builder.Property(organization => organization.UpdatedAtUtc);

        builder.HasMany(organization => organization.Members)
            .WithOne(member => member.Organization)
            .HasForeignKey(member => member.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(organization => organization.Teams)
            .WithOne(team => team.Organization)
            .HasForeignKey(team => team.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(organization => organization.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(organization => organization.Teams)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
