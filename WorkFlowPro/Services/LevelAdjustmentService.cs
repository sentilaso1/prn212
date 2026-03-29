using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public sealed class LevelAdjustmentService : ILevelAdjustmentService
{
    private readonly WorkFlowProDbContext _db;
    private readonly INotificationService _notifications;

    public LevelAdjustmentService(WorkFlowProDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<LevelAdjustmentResult> ProposeLevelChangeAsync(
        Guid workspaceId,
        string targetUserId,
        string requestedByUserId,
        MemberLevel proposedLevel,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new LevelAdjustmentResult(false, "Vui lòng nhập lý do đề xuất.");

        // 1. Kiểm tra PM có trong workspace không
        var isPm = await _db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == workspaceId && m.UserId == requestedByUserId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
        if (!isPm)
            return new LevelAdjustmentResult(false, "Bạn không có quyền thực hiện hành động này.");

        // 2. Kiểm tra Member có trong workspace không
        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);
        if (member == null)
            return new LevelAdjustmentResult(false, "Thành viên này không thuộc đơn vị của bạn.");

        // 3. Kiểm tra level hiện tại
        var profile = await _db.MemberProfiles
            .FirstOrDefaultAsync(p => p.UserId == targetUserId, cancellationToken);
        if (profile != null && profile.Level == proposedLevel)
            return new LevelAdjustmentResult(false, "Level đề xuất phải khác với level hiện tại.");

        // 4. Kiểm tra xem đã có yêu cầu nào đang chờ không
        var hasPending = await HasPendingRequestAsync(workspaceId, targetUserId, cancellationToken);
        if (hasPending)
            return new LevelAdjustmentResult(false, "Đã có một yêu cầu thay đổi level cho thành viên này đang chờ duyệt.");

        // 5. Kiểm tra xem member có task nào được đánh giá chưa (Cảnh báo nhưng vẫn cho phép)
        // SRS nói: "This member has no evaluation data. Are you sure you want to proceed?" 
        // Trong service, chúng ta cứ tạo, frontend sẽ xử lý cảnh báo nếu cần.
        
        // 6. Tạo yêu cầu
        var request = new LevelAdjustmentRequest
        {
            WorkspaceId = workspaceId,
            TargetUserId = targetUserId,
            RequestedByUserId = requestedByUserId,
            ProposedLevel = proposedLevel,
            Reason = reason.Trim(),
            Status = LevelAdjustmentRequestStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LevelAdjustmentRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        // 7. Thông báo cho Member
        await _notifications.CreateAndPushAsync(
            targetUserId,
            NotificationType.LevelAdjustmentRequest,
            "PM của bạn đã gửi đề xuất thay đổi Level. Hiện đang chờ Admin duyệt.",
            workspaceId: workspaceId,
            redirectUrl: "/Profile",
            cancellationToken: cancellationToken);

        // 8. Thông báo cho Admin (UC-12)
        // Tìm tất cả Admin hệ thống (ở đây Admin là Global Admin)
        // Theo project, Admin là những người có Role Admin trong Identity.
        // Tuy nhiên, INotificationService thường gửi cho UserId. 
        // Chúng ta có thể tạo một phương thức notify admin trong service này hoặc PlatformAdminService.
        await NotifyAdminsOfNewRequestAsync(workspaceId, cancellationToken);

        return new LevelAdjustmentResult(true);
    }

    public async Task<bool> HasPendingRequestAsync(Guid workspaceId, string targetUserId, CancellationToken cancellationToken = default)
    {
        return await _db.LevelAdjustmentRequests.AnyAsync(
            r => r.WorkspaceId == workspaceId && r.TargetUserId == targetUserId && r.Status == LevelAdjustmentRequestStatus.Pending,
            cancellationToken);
    }

    private async Task NotifyAdminsOfNewRequestAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // Lấy danh sách Admin từ Identity (ApplicationUser.Role == "Admin")
        // Ở đây giả định Admin là những user có quyền hệ thống cao nhất.
        // Tạm thời lấy tất cả user có Role Admin (nếu hệ thống có role management)
        // Hoặc đơn giản là gửi thông báo tới các Platform Admin.
        
        var admins = await _db.Users
            .Where(u => u.UserName == "admin@workflowpro.com") // Demo/Default admin
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var adminId in admins)
        {
            await _notifications.CreateAndPushAsync(
                adminId,
                NotificationType.LevelAdjustmentRequest,
                $"Có đề xuất thay đổi Level mới trong đơn vị.",
                workspaceId: workspaceId,
                redirectUrl: "/Admin",
                cancellationToken: cancellationToken);
        }
    }
}
