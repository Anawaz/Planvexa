namespace Planvexa.Modules.Automations.Domain;

using System.Globalization;
using System.Text.Json;
using Planvexa.SharedContracts.Events;

/// <summary>A single parsed automation action: a type plus a string value (often itself a small JSON
/// object for the structured action types — see each type's ApplyAsync case).</summary>
public sealed record AutomationAction(string Type, string Value)
{
    public static class Types
    {
        public const string SetStatus = "set_status";
        public const string Assign = "assign";
        public const string AddTag = "add_tag";
        public const string Notify = "notify";

        /// <summary>Sends an email to a workspace member (Value is JSON:
        /// <c>{"recipientUserId":"...","subject":"...","body":"..."}</c>, with <c>{{...}}</c> tokens
        /// interpolated from the triggering task/event — see AutomationDispatcher.Interpolate).</summary>
        public const string Email = "email";

        /// <summary>Posts a one-off signed webhook call (Value is JSON:
        /// <c>{"url":"https://..."}</c>) via the Integrations module's ad-hoc webhook pipeline.</summary>
        public const string Webhook = "webhook";

        /// <summary>Sets a task custom field (Value is JSON:
        /// <c>{"fieldId":"&lt;definition guid&gt;","value":"..."}</c>).</summary>
        public const string CustomField = "custom_field";

        /// <summary>Posts a comment on the triggering task (Value is the comment body, with
        /// <c>{{...}}</c> interpolation).</summary>
        public const string Comment = "comment";

        /// <summary>Sets the task's due date to N business days from now (Value is JSON:
        /// <c>{"days":"3"}</c>), using the workspace's working-calendar via IPlanningQueries.</summary>
        public const string SetDueDateBusinessDays = "set_due_date_business_days";

        /// <summary>STUB: a generic "call a configured integration" action. There is not yet
        /// built real third-party integrations, so this only records the attempt (Detail: "not yet
        /// implemented") and never performs a side effect — see AutomationDispatcher.ApplyAsync.</summary>
        public const string Integration = "integration";

        public static readonly IReadOnlyList<string> All = new[]
        {
            SetStatus, Assign, AddTag, Notify, Email, Webhook, CustomField, Comment, SetDueDateBusinessDays, Integration,
        };
    }
}

/// <summary>
/// Pure, deterministic automation logic: condition matching and action parsing. No I/O, no ambient
/// state — heavily unit-tested.
///
/// Conditions: a condition document is either the ORIGINAL flat format — a JSON object of
/// key→expected-value, where every key must equal (case-insensitive) the corresponding
/// <see cref="WorkspaceEvent.Data"/> value — or a NESTED tree: <c>{"and":[node,...]}</c> /
/// <c>{"or":[node,...]}</c> groups (nesting arbitrarily) of leaf nodes
/// <c>{"field":"...","equals":"..."}</c> (case-insensitive string equality, same semantics as the flat
/// key/value form) or <c>{"field":"...","gte":"123"}</c> / <c>{"field":"...","lte":"123"}</c> (decimal
/// comparison — needed by the SLA trigger's "minutesInStatus" threshold). A document is treated as a tree
/// only when its root object contains an "and"/"or"/"field" key; every existing saved rule (which never
/// has those keys at the root) keeps evaluating exactly as before — this is a pure additive extension,
/// not a format migration. An empty/absent/unparsable condition document always matches (unchanged).
///
/// Actions are a JSON array of {type,value} objects.
/// </summary>
public static class AutomationEngine
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>True if the condition document matches the event (flat legacy or nested tree — see class doc).</summary>
    public static bool Matches(string conditionJson, WorkspaceEvent workspaceEvent)
        => Matches(conditionJson, workspaceEvent.Data);

    /// <summary>Overload taking a raw data dictionary directly, so callers that only have synthesized
    /// event data (dry-run, the SLA/date sweeps building a synthetic event) don't need a full
    /// <see cref="WorkspaceEvent"/>.</summary>
    public static bool Matches(string conditionJson, IReadOnlyDictionary<string, string> data)
    {
        if (string.IsNullOrWhiteSpace(conditionJson))
        {
            return true;
        }

        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(conditionJson, Options);
        }
        catch (JsonException)
        {
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        var properties = root.EnumerateObject().ToList();
        if (properties.Count == 0)
        {
            return true;
        }

        return IsTreeNode(properties) ? EvaluateNode(root, data) : EvaluateFlatLegacy(properties, data);
    }

    private static bool IsTreeNode(List<JsonProperty> properties)
        => properties.Any(p => p.NameEquals("and") || p.NameEquals("or") || p.NameEquals("field"));

    private static bool EvaluateFlatLegacy(List<JsonProperty> properties, IReadOnlyDictionary<string, string> data)
    {
        foreach (var prop in properties)
        {
            var expected = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
            if (!data.TryGetValue(prop.Name, out var actual))
            {
                return false;
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateNode(JsonElement node, IReadOnlyDictionary<string, string> data)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (node.TryGetProperty("and", out var and))
        {
            return and.ValueKind != JsonValueKind.Array || and.EnumerateArray().All(child => EvaluateNode(child, data));
        }

        if (node.TryGetProperty("or", out var or))
        {
            return or.ValueKind == JsonValueKind.Array && or.EnumerateArray().Any(child => EvaluateNode(child, data));
        }

        if (!node.TryGetProperty("field", out var fieldEl) || fieldEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var field = fieldEl.GetString()!;
        if (!data.TryGetValue(field, out var actual))
        {
            return false;
        }

        if (node.TryGetProperty("equals", out var equalsEl))
        {
            var expected = equalsEl.ValueKind == JsonValueKind.String ? equalsEl.GetString() ?? string.Empty : equalsEl.ToString();
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        if (node.TryGetProperty("gte", out var gteEl))
        {
            return TryCompare(actual, gteEl, out var cmp) && cmp >= 0;
        }

        if (node.TryGetProperty("lte", out var lteEl))
        {
            return TryCompare(actual, lteEl, out var cmp) && cmp <= 0;
        }

        return false;
    }

    private static bool TryCompare(string actual, JsonElement expectedEl, out int comparison)
    {
        comparison = 0;
        var expectedText = expectedEl.ValueKind == JsonValueKind.String ? expectedEl.GetString() ?? string.Empty : expectedEl.ToString();
        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNum)
            || !decimal.TryParse(expectedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNum))
        {
            return false;
        }

        comparison = actualNum.CompareTo(expectedNum);
        return true;
    }

    /// <summary>Parses the flat condition object into key/value pairs for display/editing purposes only
    /// (e.g. a "legacy" editor view). Returns empty for tree-shaped or unparsable documents — callers that
    /// need to evaluate a document must use <see cref="Matches(string,WorkspaceEvent)"/>, not this.</summary>
    public static IReadOnlyDictionary<string, string> ParseConditions(string? conditionJson)
    {
        if (string.IsNullOrWhiteSpace(conditionJson))
        {
            return EmptyConditions;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(conditionJson, Options);
            if (raw is null)
            {
                return EmptyConditions;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in raw)
            {
                result[key] = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.ToString();
            }

            return result;
        }
        catch (JsonException)
        {
            return EmptyConditions;
        }
    }

    /// <summary>Parses the action array; unknown action types and unparsable input are ignored.</summary>
    public static IReadOnlyList<AutomationAction> ParseActions(string? actionJson)
    {
        if (string.IsNullOrWhiteSpace(actionJson))
        {
            return Array.Empty<AutomationAction>();
        }

        try
        {
            var raw = JsonSerializer.Deserialize<List<ActionDto>>(actionJson, Options);
            if (raw is null)
            {
                return Array.Empty<AutomationAction>();
            }

            return raw
                .Where(a => !string.IsNullOrWhiteSpace(a.Type) && AutomationAction.Types.All.Contains(a.Type!))
                .Select(a => new AutomationAction(a.Type!, a.Value ?? string.Empty))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<AutomationAction>();
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyConditions =
        new Dictionary<string, string>();

    private sealed record ActionDto(string? Type, string? Value);
}
