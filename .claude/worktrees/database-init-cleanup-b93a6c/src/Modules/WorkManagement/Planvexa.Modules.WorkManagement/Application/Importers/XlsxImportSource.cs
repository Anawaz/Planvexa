namespace Planvexa.Modules.WorkManagement.Application.Importers;

using System.IO.Compression;
using System.Xml.Linq;

/// <summary>
/// A minimal, dependency-free .xlsx reader for a flat single-sheet grid — the reading counterpart of
/// Forms' <c>FormsXlsxWriter</c>, same "Excel Open XML is just a zip of small XML parts, so
/// <see cref="ZipArchive"/> + <see cref="XDocument"/> (both stdlib) already do the job, no NuGet package
/// needed" reasoning (AGENTS.md rule 16). Reads the first worksheet only, resolving shared-string and
/// inline-string cells; numeric/date cells are read as their raw stored text (Excel serial dates are not
/// decoded — a date column should be entered/formatted as text in the source sheet).
/// ponytail: first sheet only, no styles/formulas/merged cells — reach for ClosedXML or
/// DocumentFormat.OpenXml (both MIT) if a future change needs multi-sheet or formula-aware import.
/// </summary>
public sealed class XlsxImportSource : IImportSource
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public string SourceType => "Xlsx";

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

    public static List<List<string>> ParseTable(Stream content)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(zip);
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("The workbook has no first worksheet (xl/worksheets/sheet1.xml).");

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);

        var table = new List<List<string>>();
        foreach (var rowEl in sheetDoc.Descendants(Main + "row"))
        {
            var row = new List<string>();
            var nextColumnIndex = 0;
            foreach (var cellEl in rowEl.Elements(Main + "c"))
            {
                var reference = (string?)cellEl.Attribute("r");
                var columnIndex = reference is null ? nextColumnIndex : ColumnIndexFromReference(reference);
                while (row.Count < columnIndex)
                {
                    row.Add(string.Empty);
                }

                row.Add(ReadCellValue(cellEl, sharedStrings));
                nextColumnIndex = columnIndex + 1;
            }

            table.Add(row);
        }

        return table;
    }

    private static string ReadCellValue(XElement cellEl, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cellEl.Attribute("t");
        if (type == "inlineStr")
        {
            return cellEl.Element(Main + "is")?.Element(Main + "t")?.Value ?? string.Empty;
        }

        var raw = cellEl.Element(Main + "v")?.Value;
        if (raw is null)
        {
            return string.Empty;
        }

        if (type == "s" && int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return raw;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    /// <summary>"B7" -> 1 (0-based column index; A=0, B=1, ... Z=25, AA=26, ...).</summary>
    private static int ColumnIndexFromReference(string reference)
    {
        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index - 1;
    }
}
