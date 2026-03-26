using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IUserWorkspaceService
{
    Task<IReadOnlyList<WorkspaceSwitcherItem>> GetUserWorkspacesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetFirstWorkspaceIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsUserMemberOfWorkspaceAsync(
        string userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceSwitcherItem(Guid Id, string Name, WorkspaceMemberRole Role);

public sealed class UserWorkspaceService : IUserWorkspaceService
{
    private readonly WorkFlowProDbContext _db;

    public UserWorkspaceService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WorkspaceSwitcherItem>> GetUserWorkspacesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var raw = await _db.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .Join(
                _db.Workspaces,
                m => m.WorkspaceId,
                w => w.Id,
                (m, w) => new { w.Id, w.Name, m.Role })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return raw.Select(x => new WorkspaceSwitcherItem(x.Id, x.Name, x.Role)).ToList();
    }

    public async Task<Guid?> GetFirstWorkspaceIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _db.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsUserMemberOfWorkspaceAsync(
        string userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        return await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId, cancellationToken);
    }
}

