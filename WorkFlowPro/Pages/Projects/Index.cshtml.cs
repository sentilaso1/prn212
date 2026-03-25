using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize(Policy = "IsPM")]
public sealed class IndexModel : PageModel
{
    private readonly IProjectService _projectService;

    public IndexModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public IReadOnlyList<Project> Projects { get; private set; } = Array.Empty<Project>();

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "Not authenticated.";
            return;
        }

        try
        {
            Projects = await _projectService.ListForPmAsync(userId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            // Team PM only.
            Forbid();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

