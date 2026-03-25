namespace WorkFlowPro.Data;

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
    Active = 1,
    Archived = 2
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
    LevelChanged = 11
}