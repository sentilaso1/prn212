using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IWorkspaceCreationService
{
    Task<Workspace> CreateWorkspaceForExistingUserAsync(
        string userId,
        string workspaceName,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceCreationService : IWorkspaceCreationService
{
    private readonly WorkFlowProDbContext _db;

    public WorkspaceCreationService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<Workspace> CreateWorkspaceForExistingUserAsync(
        string userId,
        string workspaceName,
        CancellationToken cancellationToken = default)
    {
        var name = workspaceName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("workspaceName is required.", nameof(workspaceName));

        if (name.Length > 200)
            name = name.Substring(0, 200);

        var workspace = new Workspace
        {
            Name = name,
            Description = null
        };

        var membership = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceMemberRole.PM,
            SubRole = WorkspaceMemberRole.PM.ToString()
        };

        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(
            x => x.UserId == userId,
            cancellationToken);

        // MemberProfile là 1-1 theo UserId, nên chỉ tạo nếu chưa tồn tại.
        profile ??= new MemberProfile
        {
            UserId = userId,
            Level = MemberLevel.Junior,
            CompletionRate = 0m,
            AvgScore = 0m,
            CurrentWorkload = 0
        };

        if (_db.Entry(profile).State == EntityState.Detached)
            _db.MemberProfiles.Add(profile);

        _db.Workspaces.Add(workspace);
        _db.WorkspaceMembers.Add(membership);

        await _db.SaveChangesAsync(cancellationToken);
        return workspace;
    }
}

