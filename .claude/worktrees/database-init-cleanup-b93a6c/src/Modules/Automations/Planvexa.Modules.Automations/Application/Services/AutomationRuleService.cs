namespace Planvexa.Modules.Automations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Automations.Authorization;
using Planvexa.Modules.Automations.Domain;
using Planvexa.SharedContracts.Work;

/// <summary>Manages automation rules (CRUD, enable/disable, versioning, templates, dry-run) and exposes
/// run/dead-letter history.</summary>
public sealed class AutomationRuleService(
    AutomationsServiceContext ctx,
    IAutomationRuleStore rules,
    IAutomationRuleVersionStore versions,
    IAutomationRunStore runs,
    ITaskDirectory tasks)
    : AutomationsServiceBase(ctx)
{
    public async Task<IReadOnlyList<AutomationRuleDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await rules.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<AutomationRuleDto> CreateAsync(CreateAutomationCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rule = AutomationRule.Create(
            NewId(), workspaceId, command.Name, command.TriggerType,
            command.ConditionJson, command.ActionJson, UserId, Now, command.TriggerConfigJson);
        rules.Add(rule);
        Audit("automation.rule.created", "AutomationRule", rule.Id, new { rule.Name, rule.TriggerType });
        await SaveAsync(ct);
        return ToDto(rule);
    }

    /// <summary>Creates a rule from a static template (see <see cref="AutomationTemplates"/>).</summary>
    public async Task<AutomationRuleDto> CreateFromTemplateAsync(string templateKey, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var template = AutomationTemplates.All.FirstOrDefault(t => t.Key == templateKey)
            ?? throw new NotFoundException($"Unknown automation template '{templateKey}'.");

        var rule = AutomationRule.Create(
            NewId(), workspaceId, template.Name, template.TriggerType,
            template.ConditionJson, template.ActionJson, UserId, Now);
        rules.Add(rule);
        Audit("automation.rule.created_from_template", "AutomationRule", rule.Id, new { template.Key });
        await SaveAsync(ct);
        return ToDto(rule);
    }

    public async Task<AutomationRuleDto> UpdateAsync(Guid id, UpdateAutomationCommand command, CancellationToken ct)
    {
        var (rule, workspaceId) = await LoadForManageAsync(id, ct);

        // Versioning: snapshot the PRE-change state before applying the edit, mirroring
        // DocumentVersion's shape — a rule's history is auditable/revertible.
        versions.Add(AutomationRuleVersion.Capture(NewId(), workspaceId, rule.Id, rule, UserId, Now));

        rule.Update(command.Name, command.TriggerType, command.ConditionJson, command.ActionJson, Now, command.TriggerConfigJson);
        Audit("automation.rule.updated", "AutomationRule", rule.Id, new { rule.Name, rule.TriggerType, rule.Version });
        await SaveAsync(ct);
        return ToDto(rule);
    }

    public async Task<IReadOnlyList<AutomationRuleVersionDto>> ListVersionsAsync(Guid id, CancellationToken ct)
    {
        var (rule, _) = await LoadForManageAsync(id, ct);
        var list = await versions.ListByRuleAsync(rule.Id, ct);
        return list
            .OrderByDescending(v => v.Version)
            .Select(v => new AutomationRuleVersionDto(v.Version, v.Name, v.TriggerType, v.ConditionJson, v.ActionJson, v.TriggerConfigJson, v.ChangedByUserId, v.ChangedAtUtc))
            .ToList();
    }

    /// <summary>Reverts the rule to a prior version's fields. The revert itself is recorded as a new
    /// version (see <see cref="AutomationRule.RestoreFrom"/>), so this is undoable too.</summary>
    public async Task<AutomationRuleDto> RevertToVersionAsync(Guid id, int version, CancellationToken ct)
    {
        var (rule, workspaceId) = await LoadForManageAsync(id, ct);
        var snapshot = await versions.FindAsync(rule.Id, version, ct)
            ?? throw new NotFoundException($"Version {version} not found for this rule.");

        versions.Add(AutomationRuleVersion.Capture(NewId(), workspaceId, rule.Id, rule, UserId, Now));
        rule.RestoreFrom(snapshot, Now);
        Audit("automation.rule.reverted", "AutomationRule", rule.Id, new { toVersion = version });
        await SaveAsync(ct);
        return ToDto(rule);
    }

    public async Task<AutomationRuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var (rule, _) = await LoadForManageAsync(id, ct);
        if (enabled)
        {
            rule.Enable(Now);
        }
        else
        {
            rule.Disable(Now);
        }

        Audit(enabled ? "automation.rule.enabled" : "automation.rule.disabled", "AutomationRule", rule.Id);
        await SaveAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var (rule, _) = await LoadForManageAsync(id, ct);
        rules.Remove(rule);
        Audit("automation.rule.deleted", "AutomationRule", id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<AutomationRunDto>> ListRunsAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var rule = await rules.FindAsync(id, ct)
            ?? throw new NotFoundException("Automation rule not found.");
        if (rule.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Automation rule not found in this workspace.");
        }

        var list = await runs.ListByRuleAsync(id, 100, ct);
        return list.Select(ToRunDto).ToList();
    }

    /// <summary>Dry-runs the rule against sample event data — no side effects. Reports whether
    /// the (possibly nested) conditions matched and, if so, the actions that WOULD have executed with
    /// their parameters. Never calls any cross-module write API.</summary>
    public async Task<AutomationDryRunResultDto> DryRunAsync(Guid id, DryRunAutomationCommand command, CancellationToken ct)
    {
        var (rule, _) = await LoadForManageAsync(id, ct);

        var sampleData = command.SampleEventData ?? new Dictionary<string, string>();
        var matched = AutomationEngine.Matches(rule.ConditionJson, sampleData);
        if (!matched)
        {
            return new AutomationDryRunResultDto(false, Array.Empty<string>());
        }

        string? taskTitle = null;
        if (command.SampleTaskId is { } sampleTaskId)
        {
            taskTitle = (await tasks.FindAsync(sampleTaskId, ct))?.Title;
        }

        var actions = AutomationEngine.ParseActions(rule.ActionJson);
        var preview = actions
            .Select(a => taskTitle is null
                ? $"{a.Type}: {a.Value}"
                : $"{a.Type}: {a.Value} (on task \"{taskTitle}\")")
            .ToList();

        return new AutomationDryRunResultDto(true, preview);
    }

    /// <summary>Dead-lettered runs for the workspace (admin visibility).</summary>
    public async Task<IReadOnlyList<AutomationRunDto>> ListDeadLettersAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var list = await runs.ListDeadLettersAsync(workspaceId, ct);
        return list.Select(ToRunDto).ToList();
    }

    /// <summary>Manually re-arms a dead-lettered (or still-failing) run for one more immediate
    /// retry attempt, picked up by the next AutomationRetryBackgroundService tick.</summary>
    public async Task<AutomationRunDto> RetryRunAsync(Guid runId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var run = await runs.FindAsync(runId, ct) ?? throw new NotFoundException("Automation run not found.");
        if (run.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Automation run not found in this workspace.");
        }

        run.RearmForManualRetry(Now);
        Audit("automation.run.manual_retry", "AutomationRun", run.Id);
        await SaveAsync(ct);
        return ToRunDto(run);
    }

    private async Task<(AutomationRule Rule, Guid WorkspaceId)> LoadForManageAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AutomationsAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rule = await rules.FindAsync(id, ct)
            ?? throw new NotFoundException("Automation rule not found.");
        if (rule.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Automation rule not found in this workspace.");
        }

        return (rule, workspaceId);
    }

    private static AutomationRuleDto ToDto(AutomationRule r)
        => new(r.Id, r.Name, r.TriggerType, r.IsEnabled, r.ConditionJson, r.ActionJson, r.TriggerConfigJson, r.Version);

    private static AutomationRunDto ToRunDto(AutomationRun r)
        => new(r.Id, r.RuleId, r.Status.ToString(), r.Detail, r.OccurredAtUtc, r.Attempts, r.NextRetryAtUtc);
}
