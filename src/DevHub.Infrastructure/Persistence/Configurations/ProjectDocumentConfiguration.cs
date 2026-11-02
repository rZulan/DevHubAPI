using DevHub.Domain.Projects;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.ToTable("ProjectDocuments", table =>
        {
            table.HasCheckConstraint("CK_ProjectDocuments_ContentJson", "[ContentJson] IS NULL OR ISJSON([ContentJson]) = 1");
            table.HasCheckConstraint("CK_ProjectDocuments_Revision", "[Revision] >= 0");
            table.HasCheckConstraint("CK_ProjectDocuments_ToolType", "[ToolType] IN ('ideas', 'erd', 'ui-editor', 'sdlc', 'notes', 'wiki', 'calendar', 'state-machine', 'uml-class', 'uml-sequence', 'uml-use-case', 'uml-activity', 'uml')");
        });
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();
        builder.Property(document => document.ToolType).HasMaxLength(30).IsRequired();
        builder.Property(document => document.ContentJson).HasColumnType("nvarchar(max)");
        builder.Property(document => document.Revision).IsConcurrencyToken();
        // Preserve the existing one-document-per-tool API until multiple documents are introduced.
        builder.HasIndex(document => new { document.ProjectId, document.ToolType }).IsUnique();
        builder.HasOne<Project>().WithMany().HasForeignKey(document => document.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(document => document.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
