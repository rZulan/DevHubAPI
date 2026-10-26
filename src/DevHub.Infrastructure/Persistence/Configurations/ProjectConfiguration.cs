using DevHub.Domain.Projects;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name).HasMaxLength(150).IsRequired();
        builder.Property(project => project.NormalizedName).HasMaxLength(150).IsRequired();
        builder.HasIndex(project => new { project.OrganizationId, project.NormalizedName }).IsUnique();
        builder.Property(project => project.Summary).HasMaxLength(2000).IsRequired();
        builder.Property(project => project.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(project => project.TechStack).HasMaxLength(2000).IsRequired();
        builder.Property(project => project.RepositoryUrl).HasMaxLength(2048);
        builder.Property(project => project.DocumentationUrl).HasMaxLength(2048);
        builder.Property(project => project.DesignUrl).HasMaxLength(2048);
        builder.Property(project => project.LiveUrl).HasMaxLength(2048);
        builder.Property(project => project.SdlcMethod).HasMaxLength(150);
        builder.Property(project => project.DatabaseDetails).HasMaxLength(500);
        builder.Property(project => project.Environments).HasMaxLength(1000).IsRequired();
        builder.Property(project => project.StartDate).HasColumnType("date");
        builder.Property(project => project.TargetDate).HasColumnType("date");
        builder.Property(project => project.CreatedAtUtc).IsRequired();
        builder.Property(project => project.UpdatedAtUtc);

        builder.HasOne(project => project.Organization)
            .WithMany()
            .HasForeignKey(project => project.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(project => project.OrganizationId);

        builder.HasOne(project => project.Team)
            .WithMany()
            .HasForeignKey(project => project.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(project => project.TeamId);

        builder.HasOne<User>(project => project.Lead)
            .WithMany()
            .HasForeignKey(project => project.LeadUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(project => project.LeadUserId);
    }
}
