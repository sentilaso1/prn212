using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;

namespace WorkFlowPro.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspace;

    public ProjectsController(WorkFlowProDbContext db, ICurrentWorkspaceService currentWorkspace)
    {
        _db = db;
        _currentWorkspace = currentWorkspace;
    }

    private bool TryResolveWorkspaceId(out Guid workspaceId, out ActionResult? errorResult)
    {
        var c = User.TryGetWorkspaceIdFromClaims();
        if (c is { } wc)
        {
            workspaceId = wc;
            errorResult = null;
            return true;
        }

        var s = _currentWorkspace.CurrentWorkspaceId;
        if (s is { } ws)
        {
            workspaceId = ws;
            errorResult = null;
            return true;
        }

        workspaceId = default;
        errorResult = BadRequest(new { error = "Thiếu workspace. Chọn đơn vị trên menu hoặc ?workspaceId=..." });
        return false;
    }

    public sealed record CreateProjectRequest(
        string Name,
        string? Description,
        string? Color,
        DateTime? StartDateUtc,
        DateTime? EndDateUtc);

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List()
    {
        if (!TryResolveWorkspaceId(out var workspaceId, out var wsErr)) return wsErr!;

        var projects = await _db.Projects
            .Where(p => p.WorkspaceId == workspaceId && p.Status == ProjectStatus.Active)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.Name, p.Description, p.Color, p.StartDateUtc, p.EndDateUtc })
            .ToListAsync();

        return Ok(projects.Cast<object>().ToList());
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateProjectRequest request)
    {
        var userId = User.GetUserId();
        if (!TryResolveWorkspaceId(out var workspaceId, out var wsErr)) return wsErr!;

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);
        if (!isPm) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Project name is required.");
        var name = request.Name.Trim();

        var exists = await _db.Projects.AnyAsync(p =>
            p.WorkspaceId == workspaceId && p.Name == name && p.Status != ProjectStatus.Archived);
        if (exists) return Conflict("Project name already exists in workspace.");

        var project = new Project
        {
            WorkspaceId = workspaceId,
            Name = name,
            Description = request.Description,
            Color = request.Color,
            StartDateUtc = request.StartDateUtc,
            EndDateUtc = request.EndDateUtc,
            OwnerUserId = userId,
            Status = ProjectStatus.Active
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return Ok(new { project.Id, project.Name, project.Description, project.Color });
    }
}

