using DevHub.Domain.Chats;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");
        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Kind).HasConversion<int>().IsRequired();
        builder.Property(conversation => conversation.Name).HasMaxLength(150).IsRequired();
        builder.Property(conversation => conversation.DirectKey).HasMaxLength(65);
        builder.Property(conversation => conversation.CreatedAtUtc).IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(conversation => conversation.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(conversation => conversation.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(conversation => conversation.OrganizationId)
            .IsUnique()
            .HasFilter("[Kind] = 0");
        builder.HasIndex(conversation => conversation.TeamId)
            .IsUnique()
            .HasFilter("[TeamId] IS NOT NULL");
        builder.HasIndex(conversation => new { conversation.OrganizationId, conversation.DirectKey })
            .IsUnique()
            .HasFilter("[DirectKey] IS NOT NULL");

        builder.HasMany(conversation => conversation.Participants)
            .WithOne(participant => participant.Conversation)
            .HasForeignKey(participant => participant.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(conversation => conversation.Messages)
            .WithOne(message => message.Conversation)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(conversation => conversation.Participants)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(conversation => conversation.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
