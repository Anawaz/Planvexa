namespace Planvexa.Modules.WorkManagement.Application.Importers;

/// <summary>
/// Of the task-management platforms Planvexa can import from, only Trello
/// (<see cref="TrelloImportSource"/>) has a real parser — these three exist as the
/// <see cref="IImportSource"/> extension point (so <c>ImportJobService</c> and the API surface already
/// support them end-to-end) with a clearly-stated "not implemented" gap, never a faked success.
/// Implementing one: parse the platform's actual export format into
/// <see cref="ImportTargetFields"/>-keyed rows (see <see cref="TrelloImportSource"/> for the pattern)
/// and swap the throw for the real parse.
/// </summary>
public sealed class JiraImportSource : IImportSource
{
    public string SourceType => "Jira";

    public ParsedImportSource Parse(Stream content) =>
        throw new NotSupportedException("Jira import is not yet implemented — needs Jira's CSV/XML issue export format.");
}

public sealed class AsanaImportSource : IImportSource
{
    public string SourceType => "Asana";

    public ParsedImportSource Parse(Stream content) =>
        throw new NotSupportedException("Asana import is not yet implemented — needs Asana's JSON project export format.");
}

public sealed class ClickUpImportSource : IImportSource
{
    public string SourceType => "ClickUp";

    public ParsedImportSource Parse(Stream content) =>
        throw new NotSupportedException("ClickUp import is not yet implemented — needs ClickUp's task export format.");
}
