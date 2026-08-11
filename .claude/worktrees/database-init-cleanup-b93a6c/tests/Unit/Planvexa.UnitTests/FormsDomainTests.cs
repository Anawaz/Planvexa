namespace Planvexa.UnitTests.Forms;

using Planvexa.Modules.Forms.Authorization;
using Planvexa.Modules.Forms.Domain;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class FormConditionalLogicTests
{
    private static Form NewForm()
        => Form.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Intake", null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Field_with_no_condition_is_always_visible()
    {
        var form = NewForm();
        var field = form.AddField(Guid.CreateVersion7(), "Name", FormFieldType.Text, false, Array.Empty<string>(), 0);

        Form.IsFieldVisible(field, new Dictionary<string, string>()).ShouldBeTrue();
    }

    [Theory]
    [InlineData("yes", "yes", true)]
    [InlineData("yes", "no", false)]
    [InlineData("Yes", "yes", true)] // case-insensitive
    public void Equals_condition_matches_source_field_value(string actual, string expected, bool visible)
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Has feedback?", FormFieldType.Text, false, Array.Empty<string>(), 0);
        var dependent = form.AddField(
            Guid.CreateVersion7(), "Details", FormFieldType.LongText, false, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.Equals, conditionValue: expected);

        var values = new Dictionary<string, string> { [source.Id.ToString()] = actual };
        Form.IsFieldVisible(dependent, values).ShouldBe(visible);
    }

    [Fact]
    public void NotEquals_condition_is_the_inverse_of_equals()
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Plan", FormFieldType.Text, false, Array.Empty<string>(), 0);
        var dependent = form.AddField(
            Guid.CreateVersion7(), "Upgrade reason", FormFieldType.Text, false, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.NotEquals, conditionValue: "Enterprise");

        Form.IsFieldVisible(dependent, new Dictionary<string, string> { [source.Id.ToString()] = "Free" }).ShouldBeTrue();
        Form.IsFieldVisible(dependent, new Dictionary<string, string> { [source.Id.ToString()] = "Enterprise" }).ShouldBeFalse();
    }

    [Theory]
    [InlineData(FormFieldConditionOperator.IsEmpty, "", true)]
    [InlineData(FormFieldConditionOperator.IsEmpty, "x", false)]
    [InlineData(FormFieldConditionOperator.IsNotEmpty, "x", true)]
    [InlineData(FormFieldConditionOperator.IsNotEmpty, "", false)]
    public void IsEmpty_and_IsNotEmpty_read_the_source_field_presence(FormFieldConditionOperator op, string actual, bool visible)
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Comment", FormFieldType.Text, false, Array.Empty<string>(), 0);
        var dependent = form.AddField(
            Guid.CreateVersion7(), "Follow-up", FormFieldType.Text, false, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: op, conditionValue: null);

        Form.IsFieldVisible(dependent, new Dictionary<string, string> { [source.Id.ToString()] = actual }).ShouldBe(visible);
    }

    [Fact]
    public void Contains_condition_is_case_insensitive_substring_match()
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Notes", FormFieldType.Text, false, Array.Empty<string>(), 0);
        var dependent = form.AddField(
            Guid.CreateVersion7(), "Urgent details", FormFieldType.Text, false, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.Contains, conditionValue: "urgent");

        Form.IsFieldVisible(dependent, new Dictionary<string, string> { [source.Id.ToString()] = "This is URGENT" }).ShouldBeTrue();
        Form.IsFieldVisible(dependent, new Dictionary<string, string> { [source.Id.ToString()] = "Whenever" }).ShouldBeFalse();
    }

    [Fact]
    public void ValidateSubmission_does_not_require_a_hidden_required_field()
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Need help?", FormFieldType.Text, false, Array.Empty<string>(), 0);
        form.AddField(
            Guid.CreateVersion7(), "Describe the issue", FormFieldType.Text, required: true, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.Equals, conditionValue: "yes");

        // Source says "no" -> the required dependent field is hidden -> must NOT throw even though empty.
        Should.NotThrow(() => form.ValidateSubmission(new Dictionary<string, string> { [source.Id.ToString()] = "no" }));
    }

    [Fact]
    public void ValidateSubmission_still_requires_a_visible_required_field()
    {
        var form = NewForm();
        var source = form.AddField(Guid.CreateVersion7(), "Need help?", FormFieldType.Text, false, Array.Empty<string>(), 0);
        form.AddField(
            Guid.CreateVersion7(), "Describe the issue", FormFieldType.Text, required: true, Array.Empty<string>(), 1,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.Equals, conditionValue: "yes");

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            form.ValidateSubmission(new Dictionary<string, string> { [source.Id.ToString()] = "yes" }));
    }

    [Fact]
    public void VisibleFieldIds_excludes_only_conditionally_hidden_fields()
    {
        var form = NewForm();
        var always = form.AddField(Guid.CreateVersion7(), "Name", FormFieldType.Text, false, Array.Empty<string>(), 0);
        var source = form.AddField(Guid.CreateVersion7(), "Toggle", FormFieldType.Text, false, Array.Empty<string>(), 1);
        var hidden = form.AddField(
            Guid.CreateVersion7(), "Hidden", FormFieldType.Text, false, Array.Empty<string>(), 2,
            conditionFieldId: source.Id, conditionOperator: FormFieldConditionOperator.Equals, conditionValue: "show");

        var visible = form.VisibleFieldIds(new Dictionary<string, string> { [source.Id.ToString()] = "nope" });
        visible.ShouldContain(always.Id);
        visible.ShouldContain(source.Id);
        visible.ShouldNotContain(hidden.Id);
    }

    [Fact]
    public void Field_cannot_condition_on_itself()
    {
        var selfId = Guid.CreateVersion7();
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            FormField.Create(selfId, Guid.CreateVersion7(), "Self", FormFieldType.Text, false, Array.Empty<string>(), 0,
                conditionFieldId: selfId, conditionOperator: FormFieldConditionOperator.IsNotEmpty));
    }
}

public sealed class FormSpamHeuristicTests
{
    private static Form NewForm()
        => Form.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Intake", null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Filled_honeypot_is_always_spam_regardless_of_timing()
    {
        var form = NewForm();
        var now = DateTimeOffset.UtcNow;
        form.IsSpamSubmission("i-am-a-bot", now.AddMinutes(-5), now).ShouldBeTrue();
    }

    [Fact]
    public void Empty_honeypot_and_no_render_timestamp_is_not_spam()
    {
        var form = NewForm();
        form.IsSpamSubmission(null, null, DateTimeOffset.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void Submitting_faster_than_the_default_threshold_is_spam()
    {
        var form = NewForm();
        var rendered = DateTimeOffset.UtcNow;
        var submitted = rendered.AddSeconds(Form.DefaultMinSubmitSeconds - 1);
        form.IsSpamSubmission(null, rendered, submitted).ShouldBeTrue();
    }

    [Fact]
    public void Submitting_at_or_after_the_threshold_is_not_spam()
    {
        var form = NewForm();
        var rendered = DateTimeOffset.UtcNow;
        var submitted = rendered.AddSeconds(Form.DefaultMinSubmitSeconds);
        form.IsSpamSubmission(null, rendered, submitted).ShouldBeFalse();
    }

    [Fact]
    public void A_configured_MinSubmitSeconds_overrides_the_default()
    {
        var form = NewForm();
        form.UpdateSettings(null, null, null, null, minSubmitSeconds: 10, null, null, null, null, null, null, null, DateTimeOffset.UtcNow);

        var rendered = DateTimeOffset.UtcNow;
        form.IsSpamSubmission(null, rendered, rendered.AddSeconds(5)).ShouldBeTrue();
        form.IsSpamSubmission(null, rendered, rendered.AddSeconds(11)).ShouldBeFalse();
    }
}

public sealed class FormSubmissionLimitTests
{
    [Theory]
    [InlineData(0, null, false)]
    [InlineData(9, 10, false)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    public void Total_submission_limit_trips_at_or_above_the_configured_cap(int totalSoFar, int? max, bool over)
        => Form.IsOverTotalSubmissionLimit(totalSoFar, max).ShouldBe(over);

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, true)]
    public void Respondent_submission_limit_trips_at_or_above_the_configured_cap(int respondentSoFar, int? max, bool over)
        => Form.IsOverRespondentSubmissionLimit(respondentSoFar, max).ShouldBe(over);
}

public sealed class FormsAuthorizerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(WorkspaceRole.Guest, true)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    public void Read_requires_workspace_membership(WorkspaceRole? role, bool allowed)
        => FormsAuthorizer.CanRead(role).ShouldBe(allowed);

    // Security-critical: form builder config and submissions (incl. exports) go through
    // FormsAuthorizer.EnsureEdit, the SAME Member+ gate as authoring — never through the anonymous public
    // token path. A Guest (or a non-member, role == null) must never pass this check.
    [Theory]
    [InlineData(null, false)]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, true)]
    public void Edit_ie_authoring_and_submission_access_requires_member_or_above(WorkspaceRole? role, bool allowed)
        => FormsAuthorizer.CanEdit(role).ShouldBe(allowed);

    [Fact]
    public void EnsureEdit_throws_for_a_non_member()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => FormsAuthorizer.EnsureEdit(null));

    [Fact]
    public void EnsureEdit_throws_for_a_guest()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => FormsAuthorizer.EnsureEdit(WorkspaceRole.Guest));
}
