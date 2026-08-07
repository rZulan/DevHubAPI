using DevHub.Domain.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Name).HasMaxLength(150).IsRequired();
        builder.Property(team => team.NormalizedName).HasMaxLength(150).IsRequired();
        builder.HasIndex(team => new { team.OrganizationId, team.NormalizedName }).IsUnique();
        builder.Property(team => team.Description).HasMaxLength(1000);
        builder.Property(team => team.CreatedAtUtc).IsRequired();
        builder.Property(team => team.UpdatedAtUtc);

        builder.HasMany(team => team.Members)
            .WithOne(member => member.Team)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(team => team.Members)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
