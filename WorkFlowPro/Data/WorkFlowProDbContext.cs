using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using WorkFlowPro.Auth;

namespace WorkFlowPro.Data;

public sealed class WorkFlowProDbContext : IdentityDbContext<ApplicationUser>
{
    public WorkFlowProDbContext(DbContextOptions<WorkFlowProDbContext> options)
        : base(options)
    {
    }

    public DbSet<MemberProfile> MemberProfiles => Set<MemberProfile>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
    public DbSet<TaskEvaluation> TaskEvaluations => Set<TaskEvaluation>();
    public DbSet<TaskHistoryEntry> TaskHistoryEntries => Set<TaskHistoryEntry>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<RoleChangeLog> RoleChangeLogs => Set<RoleChangeLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<WorkspaceInviteToken> WorkspaceInviteTokens => Set<WorkspaceInviteToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MemberProfile>(e =>
        {
            e.ToTable("MemberProfiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.CompletionRate).HasPrecision(18, 4);
            e.Property(x => x.AvgScore).HasPrecision(18, 4);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Workspace>(e =>
        {
            e.ToTable("Workspaces");
            e.Property(x => x.Name).HasMaxLength(200);
        });

        builder.Entity<WorkspaceMember>(e =>
        {
            e.ToTable("WorkspaceMembers");
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.Property(x => x.SubRole).HasMaxLength(100);

            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(32);

            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).HasMaxLength(250);
        });

        builder.Entity<TaskAssignment>(e =>
        {
            e.ToTable("TaskAssignments");
            e.Property(x => x.RejectReason).HasMaxLength(2000);
            e.HasIndex(x => new { x.TaskId, x.AssigneeUserId });

            e.HasOne<TaskItem>()
                .WithMany()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskEvaluation>(e =>
        {
            e.ToTable("TaskEvaluations");
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasIndex(x => x.TaskId).IsUnique(false);
        });

        builder.Entity<TaskHistoryEntry>(e =>
        {
            e.ToTable("TaskHistory");
        });

        builder.Entity<TaskComment>(e =>
        {
            e.ToTable("Comments");
            e.Property(x => x.Content).HasMaxLength(4000);

            e.HasOne<TaskComment>()
                .WithMany()
                .HasForeignKey(x => x.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserNotification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.RedirectUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.IsRead });
        });

        builder.Entity<RoleChangeLog>(e =>
        {
            e.ToTable("RoleChangeLogs");
        });

        builder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.Property(x => x.TokenHash).HasMaxLength(200);
            e.HasIndex(x => x.UserId);
        });

        builder.Entity<WorkspaceInviteToken>(e =>
        {
            e.ToTable("WorkspaceInviteTokens");
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.TokenHash).HasMaxLength(200);
            e.HasIndex(x => x.WorkspaceId);
        });

        builder.Entity<Attachment>(e =>
        {
            e.ToTable("Attachments");
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.FileUrl).HasMaxLength(2000);
        });
    }
}

