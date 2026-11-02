using DevHub.Domain.Authentication;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class GitHubConnectionConfiguration : IEntityTypeConfiguration<GitHubConnection>
{
    public void Configure(EntityTypeBuilder<GitHubConnection> builder)
    {
        builder.ToTable("GitHubConnections");
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.Login).HasMaxLength(100);
        builder.Property(c => c.Version).IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
