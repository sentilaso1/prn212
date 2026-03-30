using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Components;

[Authorize]
public sealed class WorkspaceSwitcherViewComponent : ViewComponent
{
    private readonly IUserWorkspaceService _userWorkspaceService;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly WorkFlowProDbContext _db;

    public WorkspaceSwitcherViewComponent(
        IUserWorkspaceService userWorkspaceService,
        ICurrentWorkspaceService currentWorkspaceService,
        WorkFlowProDbContext db)
    {
        _userWorkspaceService = userWorkspaceService;
        _currentWorkspaceService = currentWorkspaceService;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Content(string.Empty);

        var items = await _userWorkspaceService.GetUserWorkspacesAsync(userId, HttpContext.RequestAborted);
        if (items.Count == 0)
        {
            var isAdmin = await IsPlatformAdminAsync(userId, HttpContext.RequestAborted);
            var emptyVm = new WorkspaceSwitcherEmptyVm { IsPlatformAdmin = isAdmin };
            return View("/Pages/Shared/_WorkspaceSwitcherEmpty.cshtml", emptyVm);
        }

        var activeId = _currentWorkspaceService.CurrentWorkspaceId ?? items[0].Id;
        var active = items.FirstOrDefault(x => x.Id == activeId) ?? items[0];
        activeId = active.Id;

        // Remove workspaceId from returnUrl to prevent duplicated/incorrect workspace switches.
        var filteredQuery = HttpContext.Request.Query
            .Where(q => !string.Equals(q.Key, "workspaceId", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)));

        var queryString = QueryString.Create(filteredQuery);
        var returnUrl = HttpContext.Request.Path + queryString;

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
            ReturnUrl = returnUrl,
            IsPlatformAdmin = await IsPlatformAdminAsync(userId, HttpContext.RequestAborted)
        };

        return View("/Pages/Shared/_WorkspaceSwitcher.cshtml", vm);
    }

    private async Task<bool> IsPlatformAdminAsync(string userId, CancellationToken cancellationToken)
    {
        if (HttpContext.User.HasClaim("platform_role", "admin"))
            return true;

        return await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsPlatformAdmin, cancellationToken);
    }
}

