using DevHub.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("OrganizationMembers");
        builder.HasKey(member => new { member.OrganizationId, member.UserId });
        builder.Property(member => member.JoinedAtUtc).IsRequired();
        builder.Property(member => member.IsOwner).IsRequired();
        builder.HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => member.UserId);
        builder.Navigation(member => member.RoleAssignments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
