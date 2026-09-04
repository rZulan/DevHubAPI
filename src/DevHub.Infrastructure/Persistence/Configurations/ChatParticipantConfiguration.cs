using DevHub.Domain.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ChatParticipantConfiguration : IEntityTypeConfiguration<ChatParticipant>
{
    public void Configure(EntityTypeBuilder<ChatParticipant> builder)
    {
        builder.ToTable("ChatParticipants");
        builder.HasKey(participant => new { participant.ConversationId, participant.UserId });
        builder.Property(participant => participant.Role).HasConversion<int>().IsRequired();
        builder.Property(participant => participant.JoinedAtUtc).IsRequired();
        builder.HasOne(participant => participant.User)
            .WithMany()
            .HasForeignKey(participant => participant.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(participant => participant.UserId);
    }
}
