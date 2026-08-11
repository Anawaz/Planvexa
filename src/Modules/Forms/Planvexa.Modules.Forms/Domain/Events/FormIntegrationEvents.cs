namespace Planvexa.Modules.Forms.Domain.Events;

using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>Raised when a public submission is accepted, translated by the composition
/// root into a <c>WorkspaceEvent</c> of type <c>form.submitted</c> so a workspace can build automations
/// reacting to it (see <c>WorkspaceEventTranslator</c> and <c>AutomationDispatcher</c>'s loop-guard carve-out).</summary>
public sealed record FormSubmittedIntegrationEvent(
    Guid WorkspaceId, Guid FormId, Guid SubmissionId, Guid? CreatedTaskId) : IntegrationEvent;
