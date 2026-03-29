namespace WorkFlowPro.Data;

/// <summary>Trạng thái tài khoản sau đăng ký (duyệt PM / từ chối).</summary>
public enum AccountStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2
}

public enum WorkspaceRoleRequestKind
{
    PromoteMemberToPm = 1,
    DemotePmToMember = 2,

    /// <summary>PM yêu cầu xóa PM khác — Admin duyệt mới gỡ khỏi đơn vị.</summary>
    RemovePmFromWorkspace = 3
}

public enum WorkspaceRoleRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum WorkspaceMemberRole
{
    PM = 1,
    Member = 2
}

public enum MemberLevel
{
    Junior = 1,
    Mid = 2,
    Senior = 3
}

public enum ProjectStatus
{
    PendingApproval = 0,
    Active = 1,
    Archived = 2,
    Rejected = 3
}

public enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum TaskStatus
{
    Unassigned = 1,
    Pending = 2,
    ToDo = 3,
    InProgress = 4,
    Review = 5,
    Done = 6,
    Cancelled = 7
}

public enum TaskAssignmentStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3
}

public enum NotificationType
{
    TaskAssignedPending = 1,
    TaskRejected = 2,
    TaskAccepted = 3,
    TaskDoneNeedsEvaluation = 4,
    TaskEvaluated = 5,
    RoleChanged = 6,
    WorkspaceInvite = 7,
    DeadlineReminder = 8,
    ProjectCreated = 9,
    ProjectDeleted = 10,
    LevelChanged = 11,

    /// <summary>UC-11: Task được kéo trên Kanban.</summary>
    TaskKanbanMoved = 12,

    /// <summary>UC-11: Comment mới trên task.</summary>
    TaskCommentAdded = 13,

    /// <summary>UC-11: Project được cập nhật.</summary>
    ProjectUpdated = 14,

    /// <summary>UC-11: Lời mời workspace được chấp nhận.</summary>
    InviteAccepted = 15,

    /// <summary>UC-03: Lời mời workspace bị từ chối.</summary>
    InviteRejected = 16,

    /// <summary>User đăng ký PM chờ Admin duyệt.</summary>
    RegistrationPendingPm = 17,

    /// <summary>Yêu cầu nâng/hạ PM trong workspace chờ Admin.</summary>
    WorkspacePmRoleRequest = 18,

    /// <summary>UC-03 Path C: thành viên bị PM xóa khỏi workspace.</summary>
    RemovedFromWorkspace = 19,

    /// <summary>UC-10: Đề xuất thay đổi Level.</summary>
    LevelAdjustmentRequest = 20
}

public enum LevelAdjustmentRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>UC-15: loại sự kiện ghi vào audit log.</summary>
public enum AdminAuditActionType
{
    PmRegistrationApproved = 1,
    PmRegistrationRejected = 2,
    LevelAdjustmentApproved = 3,
    LevelAdjustmentRejected = 4,
    PmDemotedToMember = 5,
    ProjectCreationApproved = 6,
    ProjectCreationRejected = 7,
    MemberRemovedFromWorkspace = 8
}

public enum InviteStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3
}