namespace Planvexa.Modules.WorkManagement.Application.Importers;

using System.Text;

/// <summary>
/// A minimal, dependency-free RFC 4180 CSV parser (AGENTS.md rule 16 — .NET's base class library has no
/// CSV type, and a hand-rolled quoted-field/embedded-comma/embedded-newline parser is a couple dozen
/// lines, well short of justifying a NuGet dependency like CsvHelper for this). Handles quoted fields,
/// escaped quotes (<c>""</c>), commas and newlines inside quotes, and both \r\n and \n line endings. The
/// first row is always the header.
/// </summary>
public sealed class CsvImportSource : IImportSource
{
    public string SourceType => "Csv";

    public ParsedImportSource Parse(Stream content)
    {
        var table = ParseTable(content);
        if (table.Count == 0)
        {
            return new ParsedImportSource(Array.Empty<string>(), Array.Empty<IReadOnlyDictionary<string, string>>(), null);
        }

        var header = table[0];
        var rows = new List<IReadOnlyDictionary<string, string>>(table.Count - 1);
        for (var r = 1; r < table.Count; r++)
        {
            var row = table[r];
            var dict = new Dictionary<string, string>(header.Count);
            for (var c = 0; c < header.Count; c++)
            {
                dict[header[c]] = c < row.Count ? row[c] : string.Empty;
            }

            rows.Add(dict);
        }

        return new ParsedImportSource(header, rows, ImportColumnGuesser.Guess(header));
    }

    /// <summary>Parses raw CSV text into a grid of cells (no header/data distinction yet).</summary>
    public static List<List<string>> ParseTable(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = reader.ReadToEnd();

        var table = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            table.Add(row);
            row = new List<string>();
        }

        while (i < text.Length)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(ch);
                i++;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    EndField();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    EndRow();
                    i++;
                    break;
                default:
                    field.Append(ch);
                    i++;
                    break;
            }
        }

        // Trailing field/row (files not ending in a newline).
        if (field.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        // Drop wholly-blank trailing rows (trailing newline produces one).
        return table.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Length > 0)).ToList();
    }
}

/// <summary>Best-effort header->target-field guessing shared by CSV/Excel: a column literally named (or a
/// close synonym of) a target field maps automatically so a simple sheet validates without a manual
/// mapping step; anything else is left for the user to map explicitly.</summary>
internal static class ImportColumnGuesser
{
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        [ImportTargetFields.Title] = new[] { "title", "name", "task", "summary" },
        [ImportTargetFields.Description] = new[] { "description", "desc", "details", "notes" },
        [ImportTargetFields.StatusName] = new[] { "status" },
        [ImportTargetFields.PriorityName] = new[] { "priority" },
        [ImportTargetFields.DueDate] = new[] { "duedate", "due date", "due", "deadline" },
        [ImportTargetFields.Tags] = new[] { "tags", "labels" },
        [ImportTargetFields.ListName] = new[] { "list", "listname", "list name" },
        [ImportTargetFields.SpaceName] = new[] { "space", "spacename", "space name", "project" },
    };

    public static IReadOnlyDictionary<string, string>? Guess(IReadOnlyList<string> header)
    {
        var mapping = new Dictionary<string, string>();
        foreach (var (target, synonyms) in Synonyms)
        {
            var match = header.FirstOrDefault(h => synonyms.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));
            if (match is not null)
            {
                mapping[target] = match;
            }
        }

        return mapping.Count > 0 ? mapping : null;
    }
}
