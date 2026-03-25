using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IWorkspaceOnboardingService
{
    /// <summary>
    /// UC-01: Tạo 1 Workspace mới + gán user vào WorkspaceMember(PM) + tạo MemberProfile(Level=Junior).
    /// </summary>
    Task<Workspace> CreateWorkspaceAndBootstrapUserAsync(
        string userId,
        string workspaceName,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceOnboardingService : IWorkspaceOnboardingService
{
    private readonly WorkFlowProDbContext _db;

    public WorkspaceOnboardingService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<Workspace> CreateWorkspaceAndBootstrapUserAsync(
        string userId,
        string workspaceName,
        CancellationToken cancellationToken = default)
    {
        var cleanWorkspaceName = workspaceName.Trim();
        if (string.IsNullOrWhiteSpace(cleanWorkspaceName))
            throw new ArgumentException("workspaceName is required.", nameof(workspaceName));
        // Workspace.Name có ràng buộc max length 200 (RB/ERD).
        if (cleanWorkspaceName.Length > 200)
            cleanWorkspaceName = cleanWorkspaceName.Substring(0, 200);

        var workspace = new Workspace
        {
            Name = cleanWorkspaceName,
            Description = null
        };

        var profile = new MemberProfile
        {
            UserId = userId,
            Level = MemberLevel.Junior,
            CompletionRate = 0m,
            AvgScore = 0m,
            CurrentWorkload = 0
        };

        var membership = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceMemberRole.PM,
            SubRole = WorkspaceMemberRole.PM.ToString()
        };

        _db.Workspaces.Add(workspace);
        _db.MemberProfiles.Add(profile);
        _db.WorkspaceMembers.Add(membership);

        await _db.SaveChangesAsync(cancellationToken);
        return workspace;
    }
}

