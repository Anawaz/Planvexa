namespace Planvexa.SharedContracts.IntegrationEvents;

public sealed record CommentPostedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid CommentId, Guid AuthorUserId) : IntegrationEvent;

public sealed record UserMentionedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid CommentId, Guid MentionedUserId, Guid ByUserId) : IntegrationEvent;
