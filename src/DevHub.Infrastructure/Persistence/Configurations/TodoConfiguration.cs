using DevHub.Domain.Projects;
using DevHub.Domain.Todos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Persistence.Configurations;

internal sealed class TodoConfiguration : IEntityTypeConfiguration<Todo>
{
    public void Configure(EntityTypeBuilder<Todo> builder)
    {
        builder.ToTable("Todos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.TargetDate).HasColumnType("date");
        builder.Property(x => x.ColumnsJson).HasColumnType("nvarchar(max)").HasDefaultValue(Todo.DefaultColumnsJson).IsRequired();
        builder.Property(x => x.BoardRevision).HasDefaultValue(0).IsConcurrencyToken();
        builder.Ignore(x => x.Columns);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.TodoId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TodoTaskConfiguration : IEntityTypeConfiguration<TodoTask>
{
    public void Configure(EntityTypeBuilder<TodoTask> builder)
    {
        builder.ToTable("TodoTasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasOne<DevHub.Domain.Users.User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(x => x.Assignees).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TodoTaskAssigneeConfiguration : IEntityTypeConfiguration<TodoTaskAssignee>
{
    public void Configure(EntityTypeBuilder<TodoTaskAssignee> builder)
    {
        builder.ToTable("TodoTaskAssignees");
        builder.HasKey(x => new { x.TaskId, x.UserId });
        builder.HasOne<DevHub.Domain.Users.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
