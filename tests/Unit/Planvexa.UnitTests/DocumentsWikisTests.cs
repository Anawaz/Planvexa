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

/// <summary>DocumentComment.Create's guards — mirrors ClipComment's (untested) shape, see
/// DocumentComment.cs's doc comment for why this is a flat, unthreaded comment.</summary>
public sealed class DocumentCommentTests
{
    [Fact]
    public void Create_trims_the_body_and_stamps_the_author_and_time()
    {
        var id = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var authorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var comment = DocumentComment.Create(id, workspaceId, documentId, authorId, "  Nice work!  ", now);

        comment.Id.ShouldBe(id);
        comment.WorkspaceId.ShouldBe(workspaceId);
        comment.DocumentId.ShouldBe(documentId);
        comment.AuthorUserId.ShouldBe(authorId);
        comment.Body.ShouldBe("Nice work!");
        comment.CreatedAtUtc.ShouldBe(now);
    }

    [Fact]
    public void Create_rejects_a_blank_body()
    {
        Should.Throw<ArgumentException>(() => DocumentComment.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "   ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_rejects_an_empty_author_id()
    {
        Should.Throw<ArgumentException>(() => DocumentComment.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty, "Body", DateTimeOffset.UtcNow));
    }
}

/// <summary>DocumentShareLink's token hashing, expiry/revocation, and password verification — same
/// guarantees as Collaboration's PublicShareLink (tasks), see DocumentShareLink.cs's doc comment for
/// why this is a Documents-module duplicate rather than a cross-module reference.</summary>
public sealed class DocumentShareLinkTests
{
    [Fact]
    public void Create_returns_a_raw_token_whose_hash_matches_the_stored_hash()
    {
        var (link, raw) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);

        raw.ShouldNotBeNullOrWhiteSpace();
        link.TokenHash.ShouldBe(DocumentShareLink.HashToken(raw));
        link.TokenHash.ShouldNotBe(raw);
    }

    [Fact]
    public void Usable_reflects_revoke_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var (link, _) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), now, TimeSpan.FromDays(1));

        link.IsUsable(now).ShouldBeTrue();
        link.IsUsable(now.AddDays(2)).ShouldBeFalse(); // expired
        link.Revoke();
        link.IsUsable(now).ShouldBeFalse(); // revoked
    }

    [Fact]
    public void No_expiry_means_usable_indefinitely_until_revoked()
    {
        var now = DateTimeOffset.UtcNow;
        var (link, _) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), now, null);

        link.IsUsable(now.AddYears(10)).ShouldBeTrue();
    }

    [Fact]
    public void No_password_set_means_any_candidate_verifies()
    {
        var (link, _) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);

        link.RequiresPassword.ShouldBeFalse();
        link.VerifyPassword(null).ShouldBeTrue();
        link.VerifyPassword("anything").ShouldBeTrue();
    }

    [Fact]
    public void SetPassword_requires_the_exact_password_and_rejects_wrong_or_missing_ones()
    {
        var (link, _) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);
        link.SetPassword("correct-horse");

        link.RequiresPassword.ShouldBeTrue();
        link.VerifyPassword("correct-horse").ShouldBeTrue();
        link.VerifyPassword("wrong").ShouldBeFalse();
        link.VerifyPassword(null).ShouldBeFalse();
    }

    [Fact]
    public void SetPassword_with_empty_value_clears_the_requirement()
    {
        var (link, _) = DocumentShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);
        link.SetPassword("something");
        link.SetPassword(null);

        link.RequiresPassword.ShouldBeFalse();
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
    public void ExtractPlainText_includes_an_images_alt_text()
    {
        const string content = """
            {"root":{"children":[
                {"type":"paragraph","children":[{"type":"text","text":"Before"}]},
                {"type":"image","imageId":"11111111-1111-1111-1111-111111111111","contentType":"image/png","altText":"A diagram"}
            ],"type":"root"}}
            """;

        var text = LexicalJson.ExtractPlainText(content);
        text.ShouldContain("Before");
        text.ShouldContain("A diagram");
    }

    [Fact]
    public void ExtractPlainText_includes_a_file_attachments_file_name()
    {
        const string content = """
            {"root":{"children":[
                {"type":"paragraph","children":[{"type":"text","text":"Before"}]},
                {"type":"file-attachment","attachmentId":"11111111-1111-1111-1111-111111111111","fileName":"report.pdf","contentType":"application/pdf","sizeBytes":1234}
            ],"type":"root"}}
            """;

        var text = LexicalJson.ExtractPlainText(content);
        text.ShouldContain("Before");
        text.ShouldContain("report.pdf");
    }

    [Fact]
    public void ExtractPlainText_includes_a_mentions_name()
    {
        const string content = """
            {"root":{"children":[
                {"type":"paragraph","children":[
                    {"type":"text","text":"Ping "},
                    {"type":"mention","userId":"11111111-1111-1111-1111-111111111111","name":"Ada Lovelace"}
                ]}
            ],"type":"root"}}
            """;

        var text = LexicalJson.ExtractPlainText(content);
        text.ShouldContain("Ping");
        text.ShouldContain("@Ada Lovelace");
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
    public void ToMarkdown_renders_check_list_items_as_gfm_task_list_items()
    {
        const string content = """
            {"root":{"children":[
                {"type":"list","listType":"check","children":[
                    {"type":"listitem","checked":true,"children":[{"type":"text","text":"Done thing","format":0}]},
                    {"type":"listitem","checked":false,"children":[{"type":"text","text":"Todo thing","format":0}]}
                ]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("- [x] Done thing");
        md.ShouldContain("- [ ] Todo thing");
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

    [Fact]
    public void ToMarkdown_renders_an_image_as_a_markdown_image_link()
    {
        var imageId = Guid.NewGuid();
        var content = "{\"root\":{\"children\":[" +
            "{\"type\":\"image\",\"imageId\":\"" + imageId + "\",\"contentType\":\"image/png\",\"altText\":\"A diagram\"}" +
            "],\"type\":\"root\"}}";

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain($"![A diagram](image://{imageId})");
    }

    [Fact]
    public void ToMarkdown_renders_a_file_attachment_as_a_markdown_link()
    {
        var attachmentId = Guid.NewGuid();
        var content = "{\"root\":{\"children\":[" +
            "{\"type\":\"file-attachment\",\"attachmentId\":\"" + attachmentId + "\",\"fileName\":\"report.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":1234}" +
            "],\"type\":\"root\"}}";

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain($"[report.pdf](attachment://{attachmentId})");
    }

    [Fact]
    public void ToMarkdown_renders_a_mention_in_the_same_wire_format_as_the_comment_editor()
    {
        var userId = Guid.NewGuid();
        var content = "{\"root\":{\"children\":[" +
            "{\"type\":\"paragraph\",\"children\":[{\"type\":\"mention\",\"userId\":\"" + userId + "\",\"name\":\"Ada Lovelace\"}]}" +
            "],\"type\":\"root\"}}";

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain($"@[Ada Lovelace]({userId})");
    }

    [Fact]
    public void ToMarkdown_renders_a_table_as_gfm_with_header_separator()
    {
        const string content = """
            {"root":{"children":[
                {"type":"table","children":[
                    {"type":"tablerow","children":[
                        {"type":"tablecell","headerState":1,"children":[{"type":"paragraph","children":[{"type":"text","text":"Name","format":0}]}]},
                        {"type":"tablecell","headerState":1,"children":[{"type":"paragraph","children":[{"type":"text","text":"Status","format":0}]}]}
                    ]},
                    {"type":"tablerow","children":[
                        {"type":"tablecell","headerState":0,"children":[{"type":"paragraph","children":[{"type":"text","text":"Alpha","format":0}]}]},
                        {"type":"tablecell","headerState":0,"children":[{"type":"paragraph","children":[{"type":"text","text":"Done","format":0}]}]}
                    ]}
                ]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("| Name | Status |");
        md.ShouldContain("| --- | --- |");
        md.ShouldContain("| Alpha | Done |");
    }

    [Fact]
    public void ToMarkdown_escapes_pipes_in_table_cells()
    {
        const string content = """
            {"root":{"children":[
                {"type":"table","children":[
                    {"type":"tablerow","children":[
                        {"type":"tablecell","headerState":1,"children":[{"type":"paragraph","children":[{"type":"text","text":"A | B","format":0}]}]}
                    ]}
                ]}
            ],"type":"root"}}
            """;

        var md = LexicalMarkdown.ToMarkdown(content);
        md.ShouldContain("A \\| B");
    }
}
