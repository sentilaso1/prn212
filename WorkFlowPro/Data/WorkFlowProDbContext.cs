using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;

namespace WorkFlowPro.Data;

public sealed class WorkFlowProDbContext : IdentityDbContext<ApplicationUser>
{
    public WorkFlowProDbContext(DbContextOptions<WorkFlowProDbContext> options)
        : base(options) { }

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
    public DbSet<LevelChangeLog> LevelChangeLogs => Set<LevelChangeLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<WorkspaceInviteToken> WorkspaceInviteTokens => Set<WorkspaceInviteToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ── MemberProfile ────────────────────────────────────────────────
        b.Entity<MemberProfile>(e =>
        {
            e.ToTable("MemberProfiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CompletionRate).HasPrecision(5, 2);
            e.Property(x => x.AvgScore).HasPrecision(4, 2);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Workspace ────────────────────────────────────────────────────
        b.Entity<Workspace>(e =>
        {
            e.ToTable("Workspaces");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── WorkspaceMember ──────────────────────────────────────────────
        b.Entity<WorkspaceMember>(e =>
        {
            e.ToTable("WorkspaceMembers");
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SubRole).HasMaxLength(100);
            e.Property(x => x.JoinedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<Workspace>()
             .WithMany()
             .HasForeignKey(x => x.WorkspaceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Project ──────────────────────────────────────────────────────
        b.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Color).HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.WorkspaceId);
            e.HasIndex(x => x.Status);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.OwnerUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Workspace>()
             .WithMany()
             .HasForeignKey(x => x.WorkspaceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TaskItem ─────────────────────────────────────────────────────
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
            e.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.DueDateUtc);

            e.HasOne<Project>()
             .WithMany()
             .HasForeignKey(x => x.ProjectId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.CreatedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TaskAssignment ───────────────────────────────────────────────
        b.Entity<TaskAssignment>(e =>
        {
            e.ToTable("TaskAssignments");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RejectReason).HasMaxLength(2000);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => new { x.TaskId, x.AssigneeUserId });
            e.HasIndex(x => x.Status);

            e.HasOne<TaskItem>()
             .WithMany()
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.AssigneeUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TaskEvaluation ───────────────────────────────────────────────
        b.Entity<TaskEvaluation>(e =>
        {
            e.ToTable("TaskEvaluations");
            e.Property(x => x.Score).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.EvaluatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TaskId);

            e.HasOne<TaskItem>()
             .WithMany()
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.PmUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TaskHistoryEntry ─────────────────────────────────────────────
        b.Entity<TaskHistoryEntry>(e =>
        {
            e.ToTable("TaskHistory");
            e.Property(x => x.Action).HasMaxLength(500).IsRequired();
            e.Property(x => x.OldValue).HasMaxLength(500);
            e.Property(x => x.NewValue).HasMaxLength(500);
            e.Property(x => x.TimestampUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => x.TimestampUtc);

            e.HasOne<TaskItem>()
             .WithMany()
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            // ActorUserId — tên thực tế trong entity của bạn
            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.ActorUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TaskComment ──────────────────────────────────────────────────
        b.Entity<TaskComment>(e =>
        {
            e.ToTable("Comments");
            e.Property(x => x.Content).HasMaxLength(4000).IsRequired();
            e.Property(x => x.IsDeleted).HasDefaultValue(false);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TaskId);

            e.HasOne<TaskItem>()
             .WithMany()
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Self-reference cho reply thread (RB-11)
            e.HasOne<TaskComment>()
             .WithMany()
             .HasForeignKey(x => x.ParentCommentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── UserNotification ─────────────────────────────────────────────
        b.Entity<UserNotification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(50);
            e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            e.Property(x => x.RedirectUrl).HasMaxLength(500);
            e.Property(x => x.IsRead).HasDefaultValue(false);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasIndex(x => x.CreatedAtUtc);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // WorkspaceId, ProjectId, TaskId nullable — không cần FK cứng,
            // dùng để redirect về đúng màn hình (UC-11)
        });

        // ── Attachment ───────────────────────────────────────────────────
        b.Entity<Attachment>(e =>
        {
            e.ToTable("Attachments");
            e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.FileUrl).HasMaxLength(2000).IsRequired();
            e.Property(x => x.FileSizeBytes).IsRequired();
            e.Property(x => x.UploadedAtUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TaskId);

            e.HasOne<TaskItem>()
             .WithMany()
             .HasForeignKey(x => x.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UploadedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── RoleChangeLog ────────────────────────────────────────────────
        b.Entity<RoleChangeLog>(e =>
        {
            e.ToTable("RoleChangeLogs");
            e.Property(x => x.OldRole).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NewRole).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TimestampUtc).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.WorkspaceId);

            e.HasOne<Workspace>()
             .WithMany()
             .HasForeignKey(x => x.WorkspaceId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.ChangedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.TargetUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── LevelChangeLog (UC-14) ───────────────────────────────────────
        b.Entity<LevelChangeLog>(e =>
        {
            e.ToTable("LevelChangeLogs");
            e.Property(x => x.OldLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NewLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ChangedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasIndex(x => x.TargetUserId);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.TargetUserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.ChangedByPmId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PasswordResetToken (SF-01) ───────────────────────────────────
        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.Property(x => x.TokenHash).HasMaxLength(200).IsRequired();

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.TokenHash).IsUnique();

            e.HasOne<ApplicationUser>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WorkspaceInviteToken (UC-03) ─────────────────────────────────
        b.Entity<WorkspaceInviteToken>(e =>
        {
            e.ToTable("WorkspaceInviteTokens");
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.SubRole).HasMaxLength(100);
            e.Property(x => x.TokenHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(x => x.WorkspaceId);
            e.HasIndex(x => x.TokenHash).IsUnique();

            e.HasOne<Workspace>()
             .WithMany()
             .HasForeignKey(x => x.WorkspaceId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}