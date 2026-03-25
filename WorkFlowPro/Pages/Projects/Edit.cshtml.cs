using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize(Policy = "IsPM")]
public sealed class EditModel : PageModel
{
    private readonly IProjectService _projectService;

    public EditModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [BindProperty]
    public Guid ProjectId { get; set; }

    public Project? Project { get; private set; }
    public bool IsArchived => Project?.Status == ProjectStatus.Archived;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Name { get; set; } = default!;

        [StringLength(2000)]
        public string? Description { get; set; }

        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }

        [RegularExpression("^#?[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be hex like #RRGGBB")]
        public string? Color { get; set; }
    }

    public async Task OnGetAsync([FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        ProjectId = projectId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            Project = await _projectService.GetForPmAsync(userId, projectId, cancellationToken);

            Input = new InputModel
            {
                Name = Project.Name,
                Description = Project.Description,
                StartDateUtc = Project.StartDateUtc,
                EndDateUtc = Project.EndDateUtc,
                Color = Project.Color
            };
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

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await _projectService.UpdateAsync(userId, ProjectId, new UpdateProjectInput
            {
                Name = Input.Name,
                Description = Input.Description,
                StartDateUtc = Input.StartDateUtc,
                EndDateUtc = Input.EndDateUtc,
                Color = Input.Color
            }, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        // Redirect back to Settings.
        return LocalRedirect($"/Projects/Settings/{ProjectId}");
    }

    public async Task<IActionResult> OnPostArchiveAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            await _projectService.ArchiveAsync(userId, ProjectId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }

        return LocalRedirect($"/Projects/Settings/{ProjectId}");
    }
}

