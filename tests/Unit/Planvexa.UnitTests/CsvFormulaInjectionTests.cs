namespace Planvexa.UnitTests.Csv;

using Shouldly;
using Xunit;

/// <summary>
/// Excel and Google Sheets EXECUTE a cell whose text begins with <c>=</c>, <c>+</c>, <c>-</c>,
/// <c>@</c>, tab or CR. Every CSV this product emits carries user-controlled text, so every CSV writer
/// has to defuse that — quoting alone is not enough, because a spreadsheet strips the quotes and then
/// evaluates what is inside.
///
/// Each module deliberately keeps its OWN internal CsvWriter (see each one's doc comment citing
/// AGENTS.md rule 7). That duplication is exactly why these tests are driven off a table of writers
/// rather than written once against a shared one: every assertion below runs against all three
/// independent implementations, so a fix applied to one copy and forgotten in another fails here. The
/// module name is part of each test's display name, so a failure says which copy drifted.
///
/// <see cref="Payload"/> is the canonical DDE example — opened in Excel, an unescaped cell launches
/// calc.exe.
/// </summary>
public sealed class CsvFormulaInjectionTests
{
    private const string Payload = "=cmd|'/c calc'!A1";

    private delegate string CsvWrite(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows);

    private static readonly Dictionary<string, CsvWrite> Writers = new()
    {
        // Audit-log export (actor display names) and governed dataset exports.
        ["Governance"] = Modules.Governance.Application.CsvWriter.Write,
        // Form submissions: arbitrary text typed by an ANONYMOUS member of the public into a public
        // form, then exported and opened by someone inside the workspace. The highest-risk of the three.
        ["Forms"] = Modules.Forms.Application.Services.CsvWriter.Write,
        // Scheduled reports are EMAILED unattended, so a poisoned cell reaches recipients' inboxes
        // without anyone choosing to export anything.
        ["Reporting"] = Modules.Reporting.Application.Services.CsvWriter.Write,
    };

    private static string WriteOneCell(string module, string value)
        => Writers[module](["Value"], [new[] { value }]);

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var module in Writers.Keys)
        {
            data.Add(module);
        }

        return data;
    }

    public static TheoryData<string, string> ModulesAndTriggers()
    {
        var data = new TheoryData<string, string>();
        foreach (var module in Writers.Keys)
        {
            foreach (var trigger in new[] { "=danger", "+danger", "-danger", "@danger", "\tdanger", "\rdanger" })
            {
                data.Add(module, trigger);
            }
        }

        return data;
    }

    public static TheoryData<string, string> ModulesAndSafeValues()
    {
        var data = new TheoryData<string, string>();
        foreach (var module in Writers.Keys)
        {
            // Note "a=b" and "person@example.test": a trigger character that is not in FIRST position is
            // harmless and must not be touched.
            foreach (var safe in new[] { "danger", "Acme Corp", "a=b", "2024-01-01", "", "person@example.test" })
            {
                data.Add(module, safe);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void A_formula_payload_is_neutralised(string module)
    {
        var csv = WriteOneCell(module, Payload);

        // Apostrophe-prefixed: spreadsheets read it as "this is text" and do not display it.
        csv.ShouldContain($"'{Payload}");
        // And the raw payload never appears at the start of the cell, which is the only position that
        // triggers evaluation.
        csv.ShouldNotContain($"\n{Payload}");
    }

    [Theory]
    [MemberData(nameof(ModulesAndTriggers))]
    public void Every_trigger_character_is_neutralised(string module, string value)
    {
        WriteOneCell(module, value).ShouldContain($"'{value[0]}");
    }

    [Theory]
    [MemberData(nameof(ModulesAndSafeValues))]
    public void An_ordinary_value_is_left_completely_alone(string module, string value)
    {
        // No apostrophe smuggled onto a value that never needed one — that would corrupt real data on
        // every export, which is a worse bug than the one being fixed.
        WriteOneCell(module, value).ShouldBe($"Value\r\n{value}");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Quoting_still_applies_after_neutralising(string module)
    {
        // A neutralised value that ALSO needs quoting must still get quoted, or the row gains a column.
        // This is why each writer neutralises BEFORE deciding whether to quote.
        WriteOneCell(module, "=SUM(A1,B1)").ShouldBe("Value\r\n\"'=SUM(A1,B1)\"");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void A_leading_carriage_return_is_neutralised_and_still_quoted(string module)
    {
        // The subtle case the ordering protects: prefixing an apostrophe adds a character but leaves the
        // CR in place, so the quoting check has to run on the neutralised value, not the original.
        WriteOneCell(module, "\r=danger").ShouldBe("Value\r\n\"'\r=danger\"");
    }
}
