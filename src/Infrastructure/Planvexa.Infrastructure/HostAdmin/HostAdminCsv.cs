namespace Planvexa.Infrastructure.HostAdmin;

using System.Text;

/// <summary>
/// Minimal RFC 4180 CSV writer for the host console's activity export.
///
/// A fourth copy of a ~30-line utility, and deliberately so: this codebase keeps one per module rather
/// than sharing it (see the doc comments on Governance's, Forms' and Reporting's own CsvWriter, which
/// all cite AGENTS.md rule 7). Consolidating them is a repo-wide decision to revisit on its own, not
/// something to smuggle into the host console.
///
/// One thing this copy does that the others do not: it neutralises spreadsheet formula injection. The
/// host export carries user-controlled display names and email addresses, and a value beginning
/// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or CR is executed as a formula when the file is opened
/// in Excel or Sheets.
/// ponytail: the three module copies have the same gap on their own exports — fixing those is a
///  separate change across three modules, not a drive-by from here.
/// </summary>
internal static class HostAdminCsv
{
    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);

        foreach (var row in rows)
        {
            builder.Append("\r\n");
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            AppendField(builder, fields[i]);
        }
    }

    private static void AppendField(StringBuilder builder, string? field)
    {
        var value = Neutralize(field ?? string.Empty);
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (!mustQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    /// <summary>
    /// Prefixes a leading formula trigger with an apostrophe — the conventional fix, which spreadsheets
    /// read as "this is text" and which plain CSV parsers see as one extra literal character.
    /// </summary>
    private static string Neutralize(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;
}
