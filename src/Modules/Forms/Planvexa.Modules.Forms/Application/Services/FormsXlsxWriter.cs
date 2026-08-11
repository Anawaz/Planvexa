namespace Planvexa.Modules.Forms.Application.Services;

using System.IO.Compression;
using System.Text;

/// <summary>
/// Minimal, dependency-free .xlsx writer for a flat header+rows grid. Excel Open XML
/// is just a zip of small XML parts — <see cref="ZipArchive"/> (stdlib) already does the zipping, so no
/// NuGet package is needed for "write one flat sheet with plain string cells" (AGENTS.md rule 16: prefer
/// existing framework capabilities over an unnecessary dependency; no package in this repo already
/// produces .xlsx).
/// ponytail: no styling/formulas/typed cells (everything is inlineStr, even numbers/dates) — reach for
/// ClosedXML or DocumentFormat.OpenXml (both MIT) if a future change needs real formatting or very large
/// sheets (sharedStrings would matter for size at that point; inlineStr is fine for a submissions export).
/// </summary>
internal static class FormsXlsxWriter
{
    public static byte[] Write(string sheetName, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(zip, "_rels/.rels", RelsXml);
            WriteEntry(zip, "xl/workbook.xml", WorkbookXml(sheetName));
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            WriteEntry(zip, "xl/styles.xml", StylesXml);
            WriteEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(header, rows));
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
        <Default Extension="xml" ContentType="application/xml"/>
        <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
        <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private const string RelsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookRelsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private const string StylesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
        <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
        <borders count="1"><border/></borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
        </styleSheet>
        """;

    private static string WorkbookXml(string sheetName) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
        <sheets><sheet name="{Escape(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private static string SheetXml(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        AppendRow(sb, 1, header);
        for (var i = 0; i < rows.Count; i++)
        {
            AppendRow(sb, i + 2, rows[i]);
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowNumber, IReadOnlyList<string> cells)
    {
        sb.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var c = 0; c < cells.Count; c++)
        {
            sb.Append("<c r=\"").Append(ColumnLetter(c + 1)).Append(rowNumber).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(Escape(cells[c]))
              .Append("</t></is></c>");
        }

        sb.Append("</row>");
    }

    /// <summary>1-based column index to spreadsheet column letters (1=A, 26=Z, 27=AA, ...).</summary>
    private static string ColumnLetter(int index)
    {
        var letters = new Stack<char>();
        while (index > 0)
        {
            index--;
            letters.Push((char)('A' + (index % 26)));
            index /= 26;
        }

        return new string(letters.ToArray());
    }

    /// <summary>Escapes XML entities and strips control characters XML 1.0 cannot represent.</summary>
    private static string Escape(string value)
    {
        var clean = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\t' or '\n' || ch >= 0x20)
            {
                clean.Append(ch);
            }
        }

        return clean.ToString()
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
