using DevHub.Domain.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.CipherText).IsRequired();
        builder.Property(message => message.Nonce).HasMaxLength(32).IsRequired();
        builder.Property(message => message.AuthenticationTag).HasMaxLength(32).IsRequired();
        builder.Property(message => message.KeyVersion).HasMaxLength(40).IsRequired();
        builder.Property(message => message.CreatedAtUtc).IsRequired();
        builder.HasOne(message => message.Sender)
            .WithMany()
            .HasForeignKey(message => message.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAtUtc });
        builder.HasIndex(message => new { message.ConversationId, message.ClientMessageId }).IsUnique();
    }
}
