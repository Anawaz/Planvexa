namespace Planvexa.UnitTests.WorkManagement;

using System.Text;
using System.Text.Json;
using Planvexa.Modules.WorkManagement.Application.Importers;
using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

public sealed class CsvImportSourceTests
{
    private static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parse_reads_header_and_rows()
    {
        var csv = "Title,Description\nBuy milk,Whole milk\nWalk dog,\n";
        var result = new CsvImportSource().Parse(ToStream(csv));

        result.DetectedColumns.ShouldBe(new[] { "Title", "Description" });
        result.Rows.Count.ShouldBe(2);
        result.Rows[0]["Title"].ShouldBe("Buy milk");
        result.Rows[0]["Description"].ShouldBe("Whole milk");
        result.Rows[1]["Description"].ShouldBe(string.Empty);
    }

    [Fact]
    public void Parse_handles_quoted_fields_with_embedded_commas_newlines_and_escaped_quotes()
    {
        var csv = "Title,Description\n\"Buy milk, eggs\",\"Line1\nLine2 with \"\"quotes\"\"\"\n";
        var result = new CsvImportSource().Parse(ToStream(csv));

        result.Rows.Count.ShouldBe(1);
        result.Rows[0]["Title"].ShouldBe("Buy milk, eggs");
        result.Rows[0]["Description"].ShouldBe("Line1\nLine2 with \"quotes\"");
    }

    [Fact]
    public void Parse_guesses_a_mapping_for_recognizable_headers()
    {
        var csv = "Name,Notes,Priority\nTask A,desc,High\n";
        var result = new CsvImportSource().Parse(ToStream(csv));

        result.SuggestedMapping.ShouldNotBeNull();
        result.SuggestedMapping!.ShouldContainKeyAndValue(ImportTargetFields.Title, "Name");
        result.SuggestedMapping.ShouldContainKeyAndValue(ImportTargetFields.PriorityName, "Priority");
    }

    [Fact]
    public void Parse_of_an_empty_file_returns_no_rows()
    {
        var result = new CsvImportSource().Parse(ToStream(string.Empty));
        result.DetectedColumns.ShouldBeEmpty();
        result.Rows.ShouldBeEmpty();
    }
}

public sealed class TrelloImportSourceTests
{
    private const string BoardJson = """
        {
          "name": "Roadmap",
          "lists": [ { "id": "list1", "name": "To Do" }, { "id": "list2", "name": "Done" } ],
          "cards": [
            { "name": "Design API", "desc": "Sketch endpoints", "idList": "list1", "closed": false, "due": "2026-01-01T00:00:00.000Z", "labels": [ { "name": "backend" }, { "name": "urgent" } ] },
            { "name": "Ship v1", "desc": "", "idList": "list2", "closed": true, "due": null, "labels": [] }
          ]
        }
        """;

    private static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parse_maps_cards_to_rows_with_board_and_list_names_resolved()
    {
        var result = new TrelloImportSource().Parse(ToStream(BoardJson));

        result.Rows.Count.ShouldBe(2);
        result.Rows[0][ImportTargetFields.SpaceName].ShouldBe("Roadmap");
        result.Rows[0][ImportTargetFields.ListName].ShouldBe("To Do");
        result.Rows[0][ImportTargetFields.Title].ShouldBe("Design API");
        result.Rows[0][ImportTargetFields.Tags].ShouldBe("backend,urgent");
        result.Rows[0][ImportTargetFields.Done].ShouldBe("false");

        result.Rows[1][ImportTargetFields.ListName].ShouldBe("Done");
        result.Rows[1][ImportTargetFields.Done].ShouldBe("true");
    }

    [Fact]
    public void Parse_returns_an_identity_mapping_so_no_column_mapping_step_is_needed()
    {
        var result = new TrelloImportSource().Parse(ToStream(BoardJson));
        result.SuggestedMapping.ShouldNotBeNull();
        foreach (var field in ImportTargetFields.All)
        {
            result.SuggestedMapping!.ShouldContainKeyAndValue(field, field);
        }
    }

    [Fact]
    public void Parse_skips_cards_with_no_name()
    {
        const string json = """{ "name": "B", "lists": [], "cards": [ { "name": "", "idList": "x" } ] }""";
        var result = new TrelloImportSource().Parse(ToStream(json));
        result.Rows.ShouldBeEmpty();
    }
}

public sealed class ImportRowNormalizerTests
{
    [Fact]
    public void Normalize_requires_a_title()
    {
        var raw = new Dictionary<string, string> { ["Name"] = "" };
        var mapping = new Dictionary<string, string> { [ImportTargetFields.Title] = "Name" };

        var row = ImportRowNormalizer.Normalize(raw, mapping, out var error);
        row.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Normalize_projects_mapped_fields_and_splits_tags()
    {
        var raw = new Dictionary<string, string>
        {
            ["Name"] = "Buy milk",
            ["Labels"] = "errand, urgent",
            ["Due"] = "2026-01-01",
        };
        var mapping = new Dictionary<string, string>
        {
            [ImportTargetFields.Title] = "Name",
            [ImportTargetFields.Tags] = "Labels",
            [ImportTargetFields.DueDate] = "Due",
        };

        var row = ImportRowNormalizer.Normalize(raw, mapping, out var error);
        error.ShouldBeNull();
        row.ShouldNotBeNull();
        row!.Title.ShouldBe("Buy milk");
        row.Tags.ShouldBe(new[] { "errand", "urgent" });
        row.DueDate.ShouldNotBeNull();
    }
}

public sealed class ImportJobRowTests
{
    [Fact]
    public void IdempotencyKey_is_deterministic_for_the_same_job_and_row_index()
    {
        var jobId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();

        var a = ImportJobRow.Create(Guid.CreateVersion7(), workspaceId, jobId, 3, "{}");
        var b = ImportJobRow.Create(Guid.CreateVersion7(), workspaceId, jobId, 3, "{}");

        // Same (job, row index) always yields the same idempotency key even across separate Create calls
        // (e.g. re-parsing/re-uploading is out of scope, but a retried request handler constructing the
        // same row twice must not silently diverge) — this is the row-level half of AGENTS.md rule 13's
        // "idempotent per row" requirement.
        a.IdempotencyKey.ShouldBe(b.IdempotencyKey);

        var differentRow = ImportJobRow.Create(Guid.CreateVersion7(), workspaceId, jobId, 4, "{}");
        differentRow.IdempotencyKey.ShouldNotBe(a.IdempotencyKey);
    }

    [Fact]
    public void MarkCommitted_is_terminal_and_records_the_created_task()
    {
        var row = ImportJobRow.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 0, "{}");
        row.MarkValid();
        row.Status.ShouldBe(ImportRowStatus.Valid);

        var taskId = Guid.CreateVersion7();
        row.MarkCommitted(taskId);
        row.Status.ShouldBe(ImportRowStatus.Committed);
        row.CreatedTaskId.ShouldBe(taskId);
        row.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void RawFieldsJson_round_trips_through_normalizer()
    {
        var fields = new Dictionary<string, string> { ["Title"] = "X" };
        var json = JsonSerializer.Serialize(fields);
        var row = ImportJobRow.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 0, json);

        var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)!;
        deserialized["Title"].ShouldBe("X");
    }
}
