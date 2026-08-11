namespace Planvexa.Modules.WorkManagement.Application.Importers;

/// <summary>The normalized target-field vocabulary a column mapping (or a structured source's own keys)
/// projects a raw row onto. Matches <c>CreateTaskCommand</c>'s scope: title/description/status/priority/
/// due date/tags, plus an optional per-row space/list override for hierarchical sources.</summary>
public static class ImportTargetFields
{
    public const string SpaceName = "SpaceName";
    public const string ListName = "ListName";
    public const string Title = "Title";
    public const string Description = "Description";
    public const string StatusName = "StatusName";
    public const string PriorityName = "PriorityName";
    public const string DueDate = "DueDate";
    public const string Tags = "Tags";
    public const string Done = "Done";
    public const string AssigneeIdentifier = "AssigneeIdentifier";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SpaceName, ListName, Title, Description, StatusName, PriorityName, DueDate, Tags, Done, AssigneeIdentifier,
    };
}

/// <summary>The result of parsing a source into rows of raw string fields, before column-mapping
/// normalization is applied (<see cref="ImportRowNormalizer"/>).</summary>
public sealed record ParsedImportSource(
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    IReadOnlyDictionary<string, string>? SuggestedMapping);

/// <summary>
/// Parses one import source format into a flat set of raw string fields per row — the shared
/// intermediate representation every source (tabular or structured) normalizes into, so
/// <see cref="ImportRowNormalizer"/> and the commit pipeline never special-case a source type. A
/// structured source (Trello) emits keys that already equal <see cref="ImportTargetFields"/> names and
/// returns an identity <see cref="ParsedImportSource.SuggestedMapping"/> so it validates without the user
/// mapping columns; a tabular source (CSV/Excel) emits raw header names and best-effort guesses a mapping
/// where a header obviously matches a target field.
/// </summary>
public interface IImportSource
{
    string SourceType { get; }

    ParsedImportSource Parse(Stream content);
}

/// <summary>Projects a raw row (<c>ImportJobRow.RawFieldsJson</c>) through a column mapping (target field
/// -> source key) into the fields the commit pipeline needs. Pure/no I/O so it is trivially unit-testable
/// and reusable across validate/commit without re-parsing the source file.</summary>
public static class ImportRowNormalizer
{
    public sealed record NormalizedRow(
        string? SpaceName, string? ListName, string Title, string? Description,
        string? StatusName, string? PriorityName, DateTimeOffset? DueDate, IReadOnlyList<string> Tags, bool Done,
        string? AssigneeIdentifier);

    /// <summary>Returns null (with <paramref name="error"/> set) when the row fails validation — currently
    /// just "Title is required", the one field every downstream Task creation needs.</summary>
    public static NormalizedRow? Normalize(
        IReadOnlyDictionary<string, string> rawFields, IReadOnlyDictionary<string, string> mapping, out string? error)
    {
        string? Field(string target) =>
            mapping.TryGetValue(target, out var sourceKey) && rawFields.TryGetValue(sourceKey, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;

        var title = Field(ImportTargetFields.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "Title is required.";
            return null;
        }

        DateTimeOffset? dueDate = null;
        var rawDue = Field(ImportTargetFields.DueDate);
        if (rawDue is not null && DateTimeOffset.TryParse(rawDue, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDue))
        {
            dueDate = parsedDue;
        }

        var tags = (Field(ImportTargetFields.Tags) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var done = bool.TryParse(Field(ImportTargetFields.Done), out var parsedDone) && parsedDone;

        error = null;
        return new NormalizedRow(
            Field(ImportTargetFields.SpaceName), Field(ImportTargetFields.ListName), title!, Field(ImportTargetFields.Description),
            Field(ImportTargetFields.StatusName), Field(ImportTargetFields.PriorityName), dueDate, tags, done,
            Field(ImportTargetFields.AssigneeIdentifier));
    }
}
