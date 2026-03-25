namespace WorkFlowPro.Auth;

public interface ICurrentWorkspaceService
{
    Guid? CurrentWorkspaceId { get; }
}

