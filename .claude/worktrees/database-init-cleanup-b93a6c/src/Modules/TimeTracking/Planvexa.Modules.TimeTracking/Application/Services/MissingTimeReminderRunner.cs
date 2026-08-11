namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Fans a workspace's missing-time-reminder policy out to its roster and notifies members whose
/// tracked time in the current period is short of the configured minimum. Invoked by
/// <c>MissingTimeReminderBackgroundService</c> under a bound workspace context -- mirrors
/// <see cref="Planvexa.Modules.Notifications.Application.DigestRunner"/>'s exact split between "what
/// to do for one workspace" (this class, testable without a running host) and "how to iterate every
/// workspace on a poll" (the background service).
/// </summary>
public sealed class MissingTimeReminderRunner(
    ITimePolicyStore policies,
    ITimeEntryStore entries,
    IWorkspaceRosterQuery roster,
    INotificationPublisher notifications)
{
    /// <summary>Cross-workspace read for the worker to iterate (mirrors IDigestPreferenceStore.ListEnabledAsync).</summary>
    public Task<IReadOnlyList<TimePolicy>> ListEnabledAsync(CancellationToken ct = default)
        => policies.ListWithReminderEnabledAsync(ct);

    /// <summary>
    /// Notifies every roster member whose tracked time in the current period is short of the policy's
    /// minimum, if the period is due. Idempotent: <see cref="INotificationPublisher"/> dedupes on a key
    /// that encodes the period, so re-running mid-period for an already-notified member is a no-op.
    /// Returns the number of reminders actually sent (new, non-duplicate).
    /// </summary>
    public async Task<int> RunAsync(TimePolicy policy, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (!MissingTimeReminderPolicy.IsPeriodDue(policy.MissingTimeReminderCadence, nowUtc, policy.WeekStartsOn))
        {
            return 0;
        }

        var (periodStart, periodEnd) = MissingTimeReminderPolicy.ResolvePeriod(policy.MissingTimeReminderCadence, nowUtc, policy.WeekStartsOn);
        var periodKey = periodStart.ToString("yyyyMMdd");
        var memberUserIds = await roster.ListActiveMemberUserIdsAsync(policy.WorkspaceId, ct);

        var sent = 0;
        foreach (var userId in memberUserIds)
        {
            var trackedSeconds = await entries.SumDurationSecondsAsync(policy.WorkspaceId, userId, periodStart, periodEnd, ct);
            if (!MissingTimeReminderPolicy.IsEligible(trackedSeconds, policy.MissingTimeReminderMinimumSeconds))
            {
                continue;
            }

            await notifications.PublishAsync(new NotificationRequest(
                userId, "time.missing_time_reminder", "TimePolicy", policy.Id, policy.WorkspaceId,
                $"missing-time:{policy.WorkspaceId}:{userId}:{periodKey}",
                new Dictionary<string, string>
                {
                    ["cadence"] = policy.MissingTimeReminderCadence.ToString(),
                    ["trackedSeconds"] = trackedSeconds.ToString(),
                    ["minimumSeconds"] = policy.MissingTimeReminderMinimumSeconds.ToString(),
                }), ct);
            sent++;
        }

        return sent;
    }
}
