namespace Planvexa.Modules.Automations.Domain;

/// <summary>The outcome of evaluating/executing an automation rule for a single triggering event.</summary>
public enum AutomationRunStatus
{
    /// <summary>All matched actions executed successfully.</summary>
    Success = 0,

    /// <summary>One or more actions failed; a retry is scheduled (see <see cref="AutomationRun.NextRetryAtUtc"/>)
    /// unless attempts are already exhausted, in which case the run goes straight to <see cref="DeadLetter"/>.</summary>
    Failed = 1,

    /// <summary>The rule matched but was not executed (e.g. over the workspace run quota).</summary>
    Skipped = 2,

    /// <summary>Retries are exhausted. Terminal — a workspace admin can inspect and manually
    /// retry via the dead-letter endpoint, which re-arms one more attempt.</summary>
    DeadLetter = 3,
}
