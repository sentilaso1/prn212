using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkFlowPro.Auth;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Components;

[Authorize]
public sealed class WorkspaceSwitcherViewComponent : ViewComponent
{
    private readonly IUserWorkspaceService _userWorkspaceService;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public WorkspaceSwitcherViewComponent(
        IUserWorkspaceService userWorkspaceService,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _userWorkspaceService = userWorkspaceService;
        _currentWorkspaceService = currentWorkspaceService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Content(string.Empty);

        var items = await _userWorkspaceService.GetUserWorkspacesAsync(userId, HttpContext.RequestAborted);
        if (items.Count == 0)
            return Content(string.Empty);

        var activeId = _currentWorkspaceService.CurrentWorkspaceId ?? items[0].Id;
        var active = items.FirstOrDefault(x => x.Id == activeId) ?? items[0];
        activeId = active.Id;

        var returnUrl = HttpContext.Request.Path + HttpContext.Request.QueryString;

        var vm = new WorkspaceSwitcherVm
        {
            ActiveWorkspaceId = activeId,
            ActiveWorkspaceName = active.Name,
            Workspaces = items.Select(i => new WorkspaceSwitcherItemVm
            {
                Id = i.Id,
                Name = i.Name,
                Role = i.Role
            }).ToList(),
            ReturnUrl = returnUrl
        };

        return View("/Pages/Shared/_WorkspaceSwitcher.cshtml", vm);
    }
}

