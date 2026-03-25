using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize(Policy = "IsPM")]
public sealed class DeleteModel : PageModel
{
    private readonly IProjectService _projectService;
    private readonly WorkFlowProDbContext _db;

    public DeleteModel(IProjectService projectService, WorkFlowProDbContext db)
    {
        _projectService = projectService;
        _db = db;
    }

    [BindProperty]
    public Guid ProjectId { get; set; }
    public Project? Project { get; private set; }
    public bool HasInProgressTasks { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync([FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        ProjectId = projectId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            Project = await _projectService.GetForPmAsync(userId, projectId, cancellationToken);
            HasInProgressTasks = await _db.Tasks.AnyAsync(t =>
                t.ProjectId == projectId &&
                t.Status == WorkFlowPro.Data.TaskStatus.InProgress,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            Forbid();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            await _projectService.DeleteAsync(userId, ProjectId, cancellationToken);
            return LocalRedirect("/Projects");
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            // reload
            Project = await _projectService.GetForPmAsync(userId, ProjectId, cancellationToken);
            return Page();
        }
    }
}

