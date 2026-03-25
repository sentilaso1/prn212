using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkFlowPro.Auth;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;

namespace WorkFlowPro.Controllers;

[ApiController]
[Route("api/workspaces")]
[Authorize]
public sealed class WorkspacesController : ControllerBase
{
    private readonly IUserWorkspaceService _userWorkspaceService;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public WorkspacesController(
        IUserWorkspaceService userWorkspaceService,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _userWorkspaceService = userWorkspaceService;
        _currentWorkspaceService = currentWorkspaceService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<object>> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        var workspaces = await _userWorkspaceService.GetUserWorkspacesAsync(
            userId,
            cancellationToken);

        var active = _currentWorkspaceService.CurrentWorkspaceId ?? workspaces.First().Id;

        return Ok(new
        {
            activeWorkspaceId = active,
            workspaces = workspaces.Select(w => new
            {
                id = w.Id,
                name = w.Name,
                role = w.Role.ToString()
            })
        });
    }
}

