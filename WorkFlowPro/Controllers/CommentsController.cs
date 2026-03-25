using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;

namespace WorkFlowPro.Controllers;

[ApiController]
[Route("api")]
public sealed class CommentsController : ControllerBase
{
    private readonly WorkFlowProDbContext _db;
    private readonly ITaskHistoryService _history;

    public CommentsController(WorkFlowProDbContext db, ITaskHistoryService history)
    {
        _db = db;
        _history = history;
    }

    public sealed record CreateCommentRequest(string Content, Guid? ParentCommentId);

    public sealed record UpdateCommentRequest(string Content);

    [Authorize]
    [HttpGet("tasks/{taskId:guid}/comments")]
    public async Task<ActionResult<object>> List(Guid taskId)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);

        if (!isPm)
        {
            var canSee = await _db.TaskAssignments.AnyAsync(a =>
                a.TaskId == taskId &&
                a.AssigneeUserId == userId &&
                a.Status == TaskAssignmentStatus.Accepted);
            if (!canSee) return Forbid();
        }

        var comments = await _db.TaskComments
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.Content,
                c.IsDeleted,
                c.ParentCommentId,
                c.CreatedAtUtc,
                c.UpdatedAtUtc,
                c.UserId
            })
            .ToListAsync();

        return Ok(comments);
    }

    [Authorize]
    [HttpPost("tasks/{taskId:guid}/comments")]
    public async Task<ActionResult<object>> Create(Guid taskId, [FromBody] CreateCommentRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Comment content is required.");

        var content = request.Content.Trim();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);

        if (!isPm)
        {
            var canSee = await _db.TaskAssignments.AnyAsync(a =>
                a.TaskId == taskId &&
                a.AssigneeUserId == userId &&
                a.Status == TaskAssignmentStatus.Accepted);
            if (!canSee) return Forbid();
        }

        var comment = new TaskComment
        {
            TaskId = taskId,
            UserId = userId,
            ParentCommentId = request.ParentCommentId,
            Content = content,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(
            taskId,
            User,
            "Thêm comment",
            oldValue: null,
            newValue: content,
            cancellationToken: HttpContext.RequestAborted);

        return Ok(new { comment.Id });
    }

    [Authorize]
    [HttpPut("comments/{commentId:guid}")]
    public async Task<ActionResult<object>> Update(Guid commentId, [FromBody] UpdateCommentRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment is null) return NotFound();
        if (comment.UserId != userId) return Forbid();
        if (comment.IsDeleted) return BadRequest("Cannot edit deleted comment.");

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required.");

        var content = request.Content.Trim();

        // Ensure comment's task belongs to current workspace
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == comment.TaskId);
        if (task is null) return NotFound();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        comment.Content = content;
        comment.UpdatedAtUtc = DateTime.UtcNow;
        _db.TaskComments.Update(comment);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(
            comment.TaskId,
            User,
            "Chỉnh sửa comment",
            oldValue: null,
            newValue: content,
            cancellationToken: HttpContext.RequestAborted);

        return Ok();
    }

    [Authorize]
    [HttpDelete("comments/{commentId:guid}")]
    public async Task<ActionResult> Delete(Guid commentId)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment is null) return NotFound();
        if (comment.UserId != userId) return Forbid();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == comment.TaskId);
        if (task is null) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        var hasReplies = await _db.TaskComments.AnyAsync(c => c.ParentCommentId == commentId);

        if (hasReplies)
        {
            // RB-11: comment cannot be deleted if it has replies; keep entry and mark deleted.
            comment.IsDeleted = true;
            comment.Content = "[Bình luận đã bị xoá]";
            comment.UpdatedAtUtc = DateTime.UtcNow;
            _db.TaskComments.Update(comment);
        }
        else
        {
            _db.TaskComments.Remove(comment);
        }

        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(
            comment.TaskId,
            User,
            "Xoá comment",
            oldValue: null,
            newValue: comment.IsDeleted ? "[Bình luận đã bị xoá]" : null,
            cancellationToken: HttpContext.RequestAborted);

        return Ok();
    }
}

