namespace Planvexa.UnitTests.Collab;

using Planvexa.Modules.Documents.Domain;
using Planvexa.Modules.Forms.Domain;
using Shouldly;
using Xunit;

public sealed class DocumentDomainTests
{
    private static Document New(string content = "v1", bool isPrivate = false, Guid owner = default)
        => Document.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            owner == default ? Guid.CreateVersion7() : owner, "Doc", content, isPrivate, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Create_captures_an_initial_version()
    {
        var doc = New("hello");
        doc.Versions.Count.ShouldBe(1);
        doc.Content.ShouldBe("hello");
    }

    [Fact]
    public void Update_appends_a_version_only_when_content_changes()
    {
        var doc = New("v1");
        doc.Update(Guid.CreateVersion7(), "Renamed", null, null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        doc.Versions.Count.ShouldBe(1); // title-only change: no new version

        doc.Update(Guid.CreateVersion7(), null, "v2", null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        doc.Versions.Count.ShouldBe(2);
        doc.Content.ShouldBe("v2");

        doc.Update(Guid.CreateVersion7(), null, "v2", null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        doc.Versions.Count.ShouldBe(2); // identical content: no new version
    }

    [Fact]
    public void Revert_restores_target_content_and_records_a_version()
    {
        var doc = New("v1");
        var firstVersion = doc.Versions.Single();
        doc.Update(Guid.CreateVersion7(), null, "v2", null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        doc.Revert(Guid.CreateVersion7(), firstVersion, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        doc.Content.ShouldBe("v1");
        doc.Versions.Count.ShouldBe(3); // initial + v2 + revert snapshot
    }

    [Fact]
    public void Private_document_is_owner_only()
    {
        var owner = Guid.CreateVersion7();
        var doc = New("x", isPrivate: true, owner: owner);
        doc.CanBeViewedBy(owner).ShouldBeTrue();
        doc.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeFalse();
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => doc.EnsureViewableBy(Guid.CreateVersion7()));
    }
}

public sealed class FormDomainTests
{
    private static Form NewForm()
        => Form.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Intake", "desc", Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Create_generates_a_public_token()
        => NewForm().PublicToken.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void ValidateSubmission_requires_required_fields()
    {
        var form = NewForm();
        var f1 = form.AddField(Guid.CreateVersion7(), "Name", FormFieldType.Text, required: true, Array.Empty<string>(), 0);
        form.AddField(Guid.CreateVersion7(), "Note", FormFieldType.LongText, required: false, Array.Empty<string>(), 1);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            form.ValidateSubmission(new Dictionary<string, string>()));

        // Provided → passes.
        form.ValidateSubmission(new Dictionary<string, string> { [f1.Id.ToString()] = "Ada" });
    }

    [Fact]
    public void BuildTaskTitle_uses_first_text_field_value_else_form_title()
    {
        var form = NewForm();
        var f1 = form.AddField(Guid.CreateVersion7(), "Summary", FormFieldType.Text, required: true, Array.Empty<string>(), 0);

        form.BuildTaskTitle(new Dictionary<string, string> { [f1.Id.ToString()] = "Bug report" }).ShouldBe("Bug report");
        form.BuildTaskTitle(new Dictionary<string, string>()).ShouldBe("Intake");
    }

    [Fact]
    public void ReplaceFields_replaces_the_set()
    {
        var form = NewForm();
        form.AddField(Guid.CreateVersion7(), "Old", FormFieldType.Text, false, Array.Empty<string>(), 0);
        form.ReplaceFields(
            new[] { new FormFieldSpec(Guid.CreateVersion7(), "New", FormFieldType.Select, true, (IReadOnlyCollection<string>)new[] { "A", "B" }, 0) },
            DateTimeOffset.UtcNow);

        form.Fields.Count.ShouldBe(1);
        form.Fields[0].Label.ShouldBe("New");
        form.Fields[0].Options.ShouldBe(new[] { "A", "B" });
    }
}
