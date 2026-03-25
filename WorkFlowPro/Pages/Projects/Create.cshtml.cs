using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize(Policy = "IsPM")]
public sealed class CreateModel : PageModel
{
    private readonly IProjectService _projectService;

    public CreateModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            var _ = await _projectService.CreateAsync(userId, new CreateProjectInput
            {
                Name = Input.Name,
                Description = Input.Description,
                StartDateUtc = Input.StartDateUtc,
                EndDateUtc = Input.EndDateUtc,
                Color = Input.Color
            }, cancellationToken);

            // Keep current workspaceId in querystring (UC-15 reload).
            var workspaceId = Request.Query["workspaceId"].ToString();
            if (!string.IsNullOrWhiteSpace(workspaceId))
                return LocalRedirect($"/Projects?workspaceId={workspaceId}");

            return LocalRedirect("/Projects");
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
    }

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
}

