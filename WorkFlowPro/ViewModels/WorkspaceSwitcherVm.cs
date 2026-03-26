using WorkFlowPro.Data;

namespace WorkFlowPro.ViewModels;

public sealed class WorkspaceSwitcherVm
{
    public required Guid ActiveWorkspaceId { get; set; }
    public required string ActiveWorkspaceName { get; set; }
    public required IReadOnlyList<WorkspaceSwitcherItemVm> Workspaces { get; set; }

    public required string ReturnUrl { get; set; }

    public bool IsPlatformAdmin { get; init; }
}

public sealed class WorkspaceSwitcherItemVm
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required WorkspaceMemberRole Role { get; set; }
}

public sealed class WorkspaceSwitcherEmptyVm
{
    public bool IsPlatformAdmin { get; init; }
}

