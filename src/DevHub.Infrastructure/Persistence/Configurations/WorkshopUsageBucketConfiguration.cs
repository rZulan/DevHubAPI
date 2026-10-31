using DevHub.Infrastructure.Workshops;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class WorkshopUsageBucketConfiguration : IEntityTypeConfiguration<WorkshopUsageBucket>
{
    public void Configure(EntityTypeBuilder<WorkshopUsageBucket> builder)
    {
        builder.ToTable("WorkshopUsageBuckets");
        builder.HasKey(b => new {b.Key, b.WindowStartUtc});
        builder.Property(b => b.Key).HasMaxLength(180);
        builder.HasIndex(b => b.ExpiresAtUtc);
    }
}
