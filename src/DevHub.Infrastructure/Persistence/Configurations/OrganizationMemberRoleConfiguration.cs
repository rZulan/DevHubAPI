using DevHub.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMemberRoleConfiguration : IEntityTypeConfiguration<OrganizationMemberRole>
{
    public void Configure(EntityTypeBuilder<OrganizationMemberRole> builder)
    {
        builder.ToTable("OrganizationMemberRoles");
        builder.HasKey(assignment => new
        {
            assignment.OrganizationId,
            assignment.UserId,
            assignment.RoleId
        });
        builder.Property(assignment => assignment.AssignedAtUtc).IsRequired();
        builder.HasOne(assignment => assignment.Member)
            .WithMany(member => member.RoleAssignments)
            .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(assignment => assignment.Role)
            .WithMany()
            .HasForeignKey(assignment => assignment.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
