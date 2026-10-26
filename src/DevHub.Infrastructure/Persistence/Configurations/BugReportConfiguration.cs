using DevHub.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class BugReportConfiguration : IEntityTypeConfiguration<BugReport>, IEntityTypeConfiguration<BugReportAttachment>, IEntityTypeConfiguration<BugCategory>, IEntityTypeConfiguration<BugReportPortal>
{
    public void Configure(EntityTypeBuilder<BugReport> builder)
    {
        builder.ToTable("BugReports"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reference).HasMaxLength(20).IsRequired(); builder.HasIndex(x => x.Reference).IsUnique();
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired(); builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(20000).IsRequired(); builder.Property(x => x.Steps).HasMaxLength(10000).IsRequired();
        builder.Property(x => x.Environment).HasMaxLength(1000).IsRequired(); builder.Property(x => x.InternalNotes).HasMaxLength(10000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired(); builder.Property(x => x.Priority).HasMaxLength(20).IsRequired(); builder.Property(x => x.Source).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new {x.ProjectId, x.SubmissionId}).IsUnique(); builder.HasIndex(x => new {x.ProjectId, x.CreatedAtUtc});
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BugCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.BugReportId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<BugReportAttachment> builder)
    {
        builder.ToTable("BugReportAttachments"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileName).HasMaxLength(200).IsRequired(); builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired(); builder.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
    }
    public void Configure(EntityTypeBuilder<BugCategory> builder)
    {
        builder.ToTable("BugCategories"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired(); builder.Property(x => x.NormalizedName).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new {x.ProjectId, x.NormalizedName}).IsUnique(); builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<BugReportPortal> builder)
    {
        builder.ToTable("BugReportPortals"); builder.HasKey(x => x.ProjectId); builder.Property(x => x.ProjectId).ValueGeneratedNever();
        builder.Property(x => x.Token).HasMaxLength(64).IsRequired(); builder.HasIndex(x => x.Token).IsUnique(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
