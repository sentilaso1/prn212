using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

public interface IProjectService
{
    Task<IReadOnlyList<Project>> ListForPmAsync(string userId, CancellationToken cancellationToken = default);
    Task<Project> CreateAsync(string userId, CreateProjectInput input, CancellationToken cancellationToken = default);
    Task<Project> GetForPmAsync(string userId, Guid projectId, CancellationToken cancellationToken = default);
    Task UpdateAsync(string userId, Guid projectId, UpdateProjectInput input, CancellationToken cancellationToken = default);
    Task ArchiveAsync(string userId, Guid projectId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string userId, Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class CreateProjectInput
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public string? Color { get; set; }
}

public sealed class UpdateProjectInput
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public string? Color { get; set; }
}

public sealed class ProjectService : IProjectService
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly IHubContext<KanbanHub> _hub;
    private readonly INotificationService _notifications;

    public ProjectService(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        IHubContext<KanbanHub> hub,
        INotificationService notifications)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _hub = hub;
        _notifications = notifications;
    }

    private async Task<Guid> RequirePmWorkspaceAsync(string userId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            throw new InvalidOperationException("Missing active workspace.");

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        if (!isPm)
            throw new UnauthorizedAccessException("User is not PM in this workspace.");

        return workspaceId.Value;
    }

    private static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return string.Empty;

        var c = color.Trim();
        if (c.StartsWith("#"))
            c = c[1..];

        // Expect exactly 6 hex digits; UI validation should guard, but normalize anyway.
        return $"#{c.ToUpperInvariant()}";
    }

    public async Task<IReadOnlyList<Project>> ListForPmAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await RequirePmWorkspaceAsync(userId, cancellationToken);

        return await _db.Projects
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Project> CreateAsync(
        string userId,
        CreateProjectInput input,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = await RequirePmWorkspaceAsync(userId, cancellationToken);

        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(input));

        if (input.StartDateUtc is not null &&
            input.EndDateUtc is not null &&
            input.StartDateUtc.Value > input.EndDateUtc.Value)
        {
            throw new ArgumentException("StartDateUtc must be <= EndDateUtc.");
        }

        var normalizedColor = NormalizeColor(input.Color);

        // Unique name in workspace (không phân biệt archived).
        var exists = await _db.Projects.AnyAsync(p =>
            p.Name == name, cancellationToken);

        if (exists)
            throw new InvalidOperationException("Project name already exists in this workspace.");

        var project = new Project
        {
            WorkspaceId = workspaceId,
            Name = name,
            Description = input.Description,
            Color = normalizedColor,
            StartDateUtc = input.StartDateUtc,
            EndDateUtc = input.EndDateUtc,
            OwnerUserId = userId,
            Status = ProjectStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);

        // UC-11: thông báo cho thành viên workspace (trừ người tạo).
        var memberIds = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId != userId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        foreach (var uid in memberIds)
        {
            await _notifications.CreateAndPushAsync(
                uid,
                NotificationType.ProjectCreated,
                $"Project \"{project.Name}\" vừa được tạo trong workspace.",
                workspaceId: workspaceId,
                projectId: project.Id,
                redirectUrl: $"/Projects/Details/{project.Id}?workspaceId={workspaceId}",
                cancellationToken: cancellationToken);
        }

        // UC-12: broadcast PROJECT_CREATED (tối thiểu).
        await _hub.Clients.All.SendAsync("PROJECT_CREATED", new
        {
            projectId = project.Id,
            workspaceId = project.WorkspaceId,
            name = project.Name
        }, cancellationToken);

        return project;
    }

    public async Task<Project> GetForPmAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await RequirePmWorkspaceAsync(userId, cancellationToken);

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
            throw new KeyNotFoundException("Project not found in current workspace.");

        return project;
    }

    public async Task UpdateAsync(
        string userId,
        Guid projectId,
        UpdateProjectInput input,
        CancellationToken cancellationToken = default)
    {
        _ = await RequirePmWorkspaceAsync(userId, cancellationToken);

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
            throw new KeyNotFoundException("Project not found in current workspace.");

        if (project.Status == ProjectStatus.Archived)
            throw new InvalidOperationException("Archived project is read-only.");

        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(input));

        if (input.StartDateUtc is not null &&
            input.EndDateUtc is not null &&
            input.StartDateUtc.Value > input.EndDateUtc.Value)
        {
            throw new ArgumentException("StartDateUtc must be <= EndDateUtc.");
        }

        var normalizedColor = NormalizeColor(input.Color);

        // Unique name (ignore current project; không phân biệt archived).
        var exists = await _db.Projects.AnyAsync(p =>
            p.Id != projectId &&
            p.Name == name, cancellationToken);

        if (exists)
            throw new InvalidOperationException("Project name already exists in this workspace.");

        project.Name = name;
        project.Description = input.Description;
        project.StartDateUtc = input.StartDateUtc;
        project.EndDateUtc = input.EndDateUtc;
        project.Color = normalizedColor;

        _db.Projects.Update(project);
        await _db.SaveChangesAsync(cancellationToken);

        var memberIds = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == project.WorkspaceId && m.UserId != userId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        foreach (var uid in memberIds)
        {
            await _notifications.CreateAndPushAsync(
                uid,
                NotificationType.ProjectUpdated,
                $"Project \"{project.Name}\" vừa được cập nhật.",
                workspaceId: project.WorkspaceId,
                projectId: project.Id,
                redirectUrl: $"/Projects/Details/{project.Id}?workspaceId={project.WorkspaceId}",
                cancellationToken: cancellationToken);
        }
    }

    public async Task ArchiveAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequirePmWorkspaceAsync(userId, cancellationToken);

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
            throw new KeyNotFoundException("Project not found in current workspace.");

        if (project.Status == ProjectStatus.Archived)
            return;

        project.Status = ProjectStatus.Archived;
        _db.Projects.Update(project);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequirePmWorkspaceAsync(userId, cancellationToken);

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
            throw new KeyNotFoundException("Project not found in current workspace.");

        // UC-12: cảnh báo nếu còn Task In Progress.
        var hasInProgress = await _db.Tasks.AnyAsync(t =>
            t.ProjectId == projectId && t.Status == WorkFlowPro.Data.TaskStatus.InProgress, cancellationToken);

        if (hasInProgress)
            throw new InvalidOperationException("Cannot delete: project has Task In Progress.");

        var wsId = project.WorkspaceId;
        var projectName = project.Name;

        var notifyUserIds = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == wsId && m.UserId != userId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        foreach (var uid in notifyUserIds)
        {
            await _notifications.CreateAndPushAsync(
                uid,
                NotificationType.ProjectDeleted,
                $"Project \"{projectName}\" đã bị xóa.",
                workspaceId: wsId,
                projectId: project.Id,
                taskId: null,
                redirectUrl: "/Projects",
                cancellationToken);
        }

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

