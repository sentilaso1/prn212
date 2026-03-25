using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Workspaces;

[Authorize]
public sealed class CreateModel : PageModel
{
    private readonly IWorkspaceCreationService _workspaceCreationService;

    public CreateModel(IWorkspaceCreationService workspaceCreationService)
    {
        _workspaceCreationService = workspaceCreationService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = default!;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var workspace = await _workspaceCreationService.CreateWorkspaceForExistingUserAsync(
            userId,
            Input.Name,
            HttpContext.RequestAborted);

        return LocalRedirect($"/Workspaces?workspaceId={workspace.Id}");
    }
}

