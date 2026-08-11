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
        var value = field ?? string.Empty;
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
}

