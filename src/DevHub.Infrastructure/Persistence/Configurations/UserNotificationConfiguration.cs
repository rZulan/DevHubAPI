using DevHub.Domain.Notifications;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("UserNotifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(40).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(150).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.OrganizationId, n.UserId, n.Id });
        builder.HasIndex(n => new { n.OrganizationId, n.UserId, n.ReadAtUtc });
    }
}
