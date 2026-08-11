namespace Planvexa.UnitTests.Collab;

using Planvexa.Modules.Documents.Domain;
using Shouldly;
using Xunit;

/// <summary>Pure cycle prevention for document wiki re-parenting — same algorithm as the
/// FolderHierarchy, ported for documents.</summary>
public sealed class DocumentHierarchyTests
{
    [Fact]
    public void Moving_a_document_under_itself_is_a_cycle()
    {
        var documentId = Guid.CreateVersion7();
        DocumentHierarchy.CreatesCycle(documentId, documentId, new Dictionary<Guid, Guid?>()).ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_document_under_its_own_descendant_is_a_cycle()
    {
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var grandchild = Guid.CreateVersion7();

        var parentById = new Dictionary<Guid, Guid?>
        {
            [child] = root,
            [grandchild] = child,
        };

        DocumentHierarchy.CreatesCycle(root, grandchild, parentById).ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_document_under_an_unrelated_document_is_not_a_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var parentById = new Dictionary<Guid, Guid?> { [a] = null, [b] = null };

        DocumentHierarchy.CreatesCycle(a, b, parentById).ShouldBeFalse();
    }

    [Fact]
    public void Moving_a_document_to_top_level_is_never_a_cycle()
    {
        DocumentHierarchy.CreatesCycle(Guid.CreateVersion7(), null, new Dictionary<Guid, Guid?>()).ShouldBeFalse();
    }

    [Fact]
    public void Deep_chains_are_still_detected()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.CreateVersion7()).ToList();
        var parentById = new Dictionary<Guid, Guid?>();
        for (var i = 1; i < ids.Count; i++)
        {
            parentById[ids[i]] = ids[i - 1];
        }

        DocumentHierarchy.CreatesCycle(ids[0], ids[^1], parentById).ShouldBeTrue();
        DocumentHierarchy.CreatesCycle(ids[^1], ids[0], parentById).ShouldBeFalse();
    }
}

/// <summary>Document.SetParent domain guard, and the Lexical JSON helpers used for search
/// snippets and Markdown export.</summary>
public sealed class DocumentParentingTests
{
    [Fact]
    public void SetParent_rejects_self_parenting()
    {
        var doc = Document.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Doc", "x", false, null, null, null, DateTimeOffset.UtcNow);
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(
            () => doc.SetParent(doc.Id, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetParent_updates_the_parent_and_touches_updated_at()
    {
        var doc = Document.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Doc", "x", false, null, null, null, DateTimeOffset.UtcNow.AddDays(-1));
        var parentId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        doc.SetParent(parentId, Guid.CreateVersion7(), now);

        doc.ParentDocumentId.ShouldBe(parentId);
        doc.UpdatedAtUtc.ShouldBe(now);
    }

    [Fact]
    public void Create_defaults_empty_content_to_an_empty_lexical_document()
    {
        var doc = Document.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Doc", "", false, null, null, null, DateTimeOffset.UtcNow);
        doc.Content.ShouldBe(LexicalJson.EmptyDocument);
    }
}

public sealed class LexicalJsonTests
{
    [Fact]
    public void ToJson_then_ExtractPlainText_round_trips_plain_text()
    {
        var json = LexicalJson.ToJson("Hello world");
        LexicalJson.ExtractPlainText(json).ShouldBe("Hello world");
    }

    [Fact]
    public void ExtractPlainText_walks_headings_and_paragraphs_with_newline_boundaries()
    {
        const string content = """
            {"root":{"children":[
                {"type":"heading","tag":"h1","children":[{"type":"text","text":"Title"}]},
                {"type":"paragraph","children":[{"type":"text","text":"Body one"}]},
                {"type":"paragraph","children":[{"type":"text","text":"Body two"}]}
            ],"type":"root"}}
            """;

        LexicalJson.ExtractPlainText(content).ShouldBe("Title\nBody one\nBody two");
    }

    [Fact]
    public void ExtractPlainText_walks_nested_lists()
    {
        const string content = """
            {"root":{"children":[
                {"type":"list","listType":"bullet","children":[
                    {"type":"listitem","children":[{"type":"text","text":"Item one"}]},
                    {"type":"listitem","children":[{"type":"text","text":"Item two"}]}
                ]}
            ],"type":"root"}}
            """;

        var text = LexicalJson.ExtractPlainText(content);
        text.ShouldContain("Item one");
        text.ShouldContain("Item two");
    }

    [Fact]
    public void ExtractPlainText_tolerates_malformed_json_by_returning_it_verbatim()
    {
        LexicalJson.ExtractPlainText("not json at all").ShouldBe("not json at all");
    }

    [Fact]
    public void ExtractPlainText_of_empty_string_is_empty()
    {
        LexicalJson.ExtractPlainText("").ShouldBe(string.Empty);
        LexicalJson.ExtractPlainText(null).ShouldBe(string.Empty);
    }
}

public sealed class LexicalMarkdownTests
{
    [Fact]
    public void ToMarkdown_renders_heading_and_paragraph()
    {
        const string content = """
            {"root":{"children":[
                {"type":"heading","tag":"h2","children":[{"type":"text","text":"Section","format":0}]},
                {"type":"paragraph","children":[{"type":"text","text":"Some body text.","format":0}]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("## Section");
        md.ShouldContain("Some body text.");
    }

    [Fact]
    public void ToMarkdown_applies_bold_and_italic_formatting()
    {
        const string content = """
            {"root":{"children":[
                {"type":"paragraph","children":[
                    {"type":"text","text":"bold","format":1},
                    {"type":"text","text":" and ","format":0},
                    {"type":"text","text":"italic","format":2}
                ]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("**bold**");
        md.ShouldContain("*italic*");
    }

    [Fact]
    public void ToMarkdown_renders_bullet_list_items()
    {
        const string content = """
            {"root":{"children":[
                {"type":"list","listType":"bullet","children":[
                    {"type":"listitem","children":[{"type":"text","text":"First","format":0}]},
                    {"type":"listitem","children":[{"type":"text","text":"Second","format":0}]}
                ]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("- First");
        md.ShouldContain("- Second");
    }

    [Fact]
    public void ToMarkdown_renders_a_task_reference_as_a_task_link()
    {
        var taskId = Guid.NewGuid();
        var content = "{\"root\":{\"children\":[" +
            "{\"type\":\"paragraph\",\"children\":[{\"type\":\"task-reference\",\"taskId\":\"" + taskId + "\",\"title\":\"Fix the bug\"}]}" +
            "],\"type\":\"root\"}}";

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain($"[Fix the bug](task://{taskId})");
    }
}
