using DevHub.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationColorSchemeConfiguration : IEntityTypeConfiguration<OrganizationColorScheme>
{
    public void Configure(EntityTypeBuilder<OrganizationColorScheme> builder)
    {
        builder.ToTable("OrganizationColorSchemes");
        builder.HasKey(scheme => scheme.Id);
        builder.Property(scheme => scheme.Name).HasMaxLength(OrganizationColorScheme.NameMaxLength).IsRequired();
        builder.Property(scheme => scheme.CreatedAtUtc).IsRequired();
        builder.Property(scheme => scheme.UpdatedAtUtc);
        builder.ComplexProperty(scheme => scheme.Light, palette => ConfigurePalette(palette, "Light"));
        builder.ComplexProperty(scheme => scheme.Dark, palette => ConfigurePalette(palette, "Dark"));
        builder.HasIndex(scheme => new { scheme.OrganizationId, scheme.UserId, scheme.Name }).IsUnique();
        // Owned by the membership, so leaving or deleting the organization removes a member's schemes.
        builder.HasOne<OrganizationMember>()
            .WithMany()
            .HasForeignKey(scheme => new { scheme.OrganizationId, scheme.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePalette(ComplexPropertyBuilder<ColorSchemePalette> palette, string mode)
    {
        palette.Property(colors => colors.Accent).HasColumnName($"{mode}Accent").HasMaxLength(7).IsUnicode(false).IsRequired();
        palette.Property(colors => colors.Background).HasColumnName($"{mode}Background").HasMaxLength(7).IsUnicode(false).IsRequired();
        palette.Property(colors => colors.Surface).HasColumnName($"{mode}Surface").HasMaxLength(7).IsUnicode(false).IsRequired();
        palette.Property(colors => colors.Sidebar).HasColumnName($"{mode}Sidebar").HasMaxLength(7).IsUnicode(false).IsRequired();
        palette.Property(colors => colors.Text).HasColumnName($"{mode}Text").HasMaxLength(7).IsUnicode(false).IsRequired();
    }
}
