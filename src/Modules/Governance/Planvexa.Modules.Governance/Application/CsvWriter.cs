namespace Planvexa.Modules.Governance.Application;

using System.Text;

internal static class CsvWriter
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

    private static void AppendField(StringBuilder builder, string field)
    {
        // Neutralize BEFORE the quoting decision: prefixing a value that starts with CR/tab adds a
        // character but leaves the control character in place, so the mustQuote check below still has
        // to see it.
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
    /// Defuses spreadsheet formula injection. Excel and Google Sheets EXECUTE a cell whose text begins
    /// with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or CR, and this module's exports carry
    /// user-controlled text: the audit log's actor display names, and whatever the governed dataset
    /// export happens to include. Quoting alone does not help — a spreadsheet strips the quotes and
    /// then evaluates what is inside.
    ///
    /// The apostrophe prefix is the conventional fix: spreadsheets read it as "treat this as text" and
    /// do not display it, while a plain CSV parser sees one extra literal character.
    /// </summary>
    private static string Neutralize(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;
}

