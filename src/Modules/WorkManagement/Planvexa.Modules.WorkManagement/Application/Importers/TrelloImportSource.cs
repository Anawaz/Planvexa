namespace Planvexa.Modules.WorkManagement.Application.Importers;

using System.Text.Json;

/// <summary>
/// Parses a Trello board JSON export (Trello → Menu → Print, export, and share → Export as JSON) — the
/// one fully-implemented platform importer: whichever platform has the simplest, most stable,
/// best-documented export format wins, and Trello's export is a
/// single flat, stable, well-documented JSON document (<c>lists</c> + <c>cards</c>, referenced by id) with
/// no auth/API-version surface to track — unlike Asana (a versioned REST API, not a static export
/// format) or Jira (CSV/XML export whose column set is instance-configurable). Each card becomes one row;
/// the board's own list structure is preserved (a Trello List becomes a Planvexa List under one Planvexa
/// Space named after the board) — real multi-level hierarchy creation, not just flat rows.
/// </summary>
public sealed class TrelloImportSource : IImportSource
{
    public string SourceType => "Trello";

    /// <summary>Identity mapping: this source's own keys already equal <see cref="ImportTargetFields"/>
    /// names, so a Trello import validates immediately with no column-mapping step required.</summary>
    public static readonly IReadOnlyDictionary<string, string> IdentityMapping = ImportTargetFields.All.ToDictionary(f => f, f => f);

    public ParsedImportSource Parse(Stream content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var boardName = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Trello Import" : "Trello Import";

        var listNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("lists", out var listsEl) && listsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var list in listsEl.EnumerateArray())
            {
                var id = list.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var name = list.TryGetProperty("name", out var listNameEl) ? listNameEl.GetString() : null;
                if (id is not null && name is not null)
                {
                    listNames[id] = name;
                }
            }
        }

        // Member id -> email/name, so a card's idMembers can resolve to something the normalizer can
        // match a workspace member against. Trello's export only includes "email" for board admins on
        // paid plans, so fullName/username are the common case.
        var memberIdentifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("members", out var membersEl) && membersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in membersEl.EnumerateArray())
            {
                var id = member.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var identifier =
                    (member.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null)
                    ?? (member.TryGetProperty("fullName", out var fullNameEl) ? fullNameEl.GetString() : null)
                    ?? (member.TryGetProperty("username", out var usernameEl) ? usernameEl.GetString() : null);
                if (id is not null && !string.IsNullOrWhiteSpace(identifier))
                {
                    memberIdentifiers[id] = identifier;
                }
            }
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        if (root.TryGetProperty("cards", out var cardsEl) && cardsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in cardsEl.EnumerateArray())
            {
                var title = card.TryGetProperty("name", out var titleEl) ? titleEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var idList = card.TryGetProperty("idList", out var idListEl) ? idListEl.GetString() : null;
                var listName = idList is not null && listNames.TryGetValue(idList, out var n) ? n : "Imported";
                var description = card.TryGetProperty("desc", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;
                var closed = card.TryGetProperty("closed", out var closedEl) && closedEl.ValueKind == JsonValueKind.True;
                var due = card.TryGetProperty("due", out var dueEl) && dueEl.ValueKind == JsonValueKind.String ? dueEl.GetString() ?? string.Empty : string.Empty;

                var tags = string.Empty;
                if (card.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
                {
                    tags = string.Join(',', labelsEl.EnumerateArray()
                        .Select(l => l.TryGetProperty("name", out var labelNameEl) ? labelNameEl.GetString() : null)
                        .Where(n => !string.IsNullOrWhiteSpace(n)));
                }

                var assignee = string.Empty;
                if (card.TryGetProperty("idMembers", out var idMembersEl) && idMembersEl.ValueKind == JsonValueKind.Array)
                {
                    var firstKnownMemberId = idMembersEl.EnumerateArray()
                        .Select(m => m.GetString())
                        .FirstOrDefault(id => id is not null && memberIdentifiers.ContainsKey(id));
                    if (firstKnownMemberId is not null)
                    {
                        assignee = memberIdentifiers[firstKnownMemberId];
                    }
                }

                rows.Add(new Dictionary<string, string>
                {
                    [ImportTargetFields.SpaceName] = boardName,
                    [ImportTargetFields.ListName] = listName,
                    [ImportTargetFields.Title] = title,
                    [ImportTargetFields.Description] = description,
                    [ImportTargetFields.Done] = closed ? "true" : "false",
                    [ImportTargetFields.DueDate] = due,
                    [ImportTargetFields.Tags] = tags,
                    [ImportTargetFields.AssigneeIdentifier] = assignee,
                });
            }
        }

        return new ParsedImportSource(ImportTargetFields.All, rows, IdentityMapping);
    }
}
