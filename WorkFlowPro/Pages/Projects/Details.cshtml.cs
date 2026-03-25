using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize(Policy = "IsPM")]
public sealed class DetailsModel : PageModel
{
    private readonly IProjectService _projectService;

    public DetailsModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public Project? Project { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync([FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            Project = await _projectService.GetForPmAsync(userId, projectId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            Forbid();
        }
        catch (KeyNotFoundException)
        {
            ErrorMessage = "Project not found.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

