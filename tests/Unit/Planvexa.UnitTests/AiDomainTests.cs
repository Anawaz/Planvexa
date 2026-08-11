namespace Planvexa.UnitTests.Ai;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Primitives;
using Planvexa.Modules.Ai.Domain;
using Planvexa.Modules.Ai.Authorization;
using Planvexa.SharedContracts.Ai;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class ExtractiveAiTests
{
    private static AiPrompt Prompt(AiTaskKind kind, string title, string? desc = null, params string[] context)
        => new(kind, title, desc, context);

    [Fact]
    public void Summarize_is_deterministic_and_bounded()
    {
        var p = Prompt(AiTaskKind.Summarize, "Fix login bug", "Users cannot log in. The token refresh fails intermittently.", "check auth service", "review logs");
        var a = ExtractiveAi.Complete(p);
        var b = ExtractiveAi.Complete(p);

        a.Text.ShouldBe(b.Text); // deterministic
        a.Text.ShouldStartWith("Fix login bug.");
        a.Text.ShouldContain("2 related notes.");
        a.Text.Length.ShouldBeLessThanOrEqualTo(500);
        a.TokensEstimated.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GenerateSubtasks_uses_context_then_description()
    {
        var withContext = ExtractiveAi.Complete(Prompt(AiTaskKind.GenerateSubtasks, "Ship release", null, "cut branch", "run tests", "tag build"));
        var titles = withContext.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        titles.Length.ShouldBe(3);
        titles.ShouldContain("cut branch");

        var fromDescription = ExtractiveAi.Complete(Prompt(AiTaskKind.GenerateSubtasks, "Plan trip", "Book flights. Reserve hotel. Rent a car."));
        fromDescription.Text.Split('\n').Length.ShouldBe(3);
    }

    [Fact]
    public void GenerateSubtasks_falls_back_when_empty()
    {
        var result = ExtractiveAi.Complete(Prompt(AiTaskKind.GenerateSubtasks, "Bare task"));
        result.Text.ShouldContain("Break down: Bare task");
    }

    [Theory]
    [InlineData("This is overdue and urgent", "Urgent")]
    [InlineData("An important blocker for the release", "High")]
    [InlineData("Someday maybe, low priority backlog", "Low")]
    [InlineData("Regular task with no signals", "Normal")]
    public void SuggestPriority_reads_signals(string text, string expected)
    {
        var result = ExtractiveAi.Complete(Prompt(AiTaskKind.SuggestPriority, "Task", text));
        result.Text.Split('|')[0].ShouldBe(expected);
    }

    [Fact]
    public void EstimateTokens_counts_words_min_one()
    {
        ExtractiveAi.EstimateTokens("two words").ShouldBe(2);
        ExtractiveAi.EstimateTokens("").ShouldBe(1);
        ExtractiveAi.EstimateTokens(null, "a b c").ShouldBe(3);
    }
}

public sealed class AiRequestTests
{
    [Fact]
    public void Record_rejects_negative_tokens()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            AiRequest.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "k", AiTaskKind.Summarize, Guid.CreateVersion7(), -1, "x", DateTimeOffset.UtcNow));

    [Fact]
    public void Record_captures_fields()
    {
        var r = AiRequest.Record(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "key-1", AiTaskKind.SuggestPriority, Guid.CreateVersion7(), 12, "High|because", DateTimeOffset.UtcNow);
        r.RequestKey.ShouldBe("key-1");
        r.Kind.ShouldBe(AiTaskKind.SuggestPriority);
        r.TokensEstimated.ShouldBe(12);
    }
}

public sealed class AiAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    public void Use_requires_member(WorkspaceRole role, bool allowed)
        => AiAuthorizer.CanUse(role).ShouldBe(allowed);

    [Fact]
    public void EnsureUse_throws_for_guest()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => AiAuthorizer.EnsureUse(WorkspaceRole.Guest));
}

// ---- Offline (ExtractiveAi) fallback behaviour for the comment/chat/document/risk capabilities ----

public sealed class ExtractiveAiFallbackTests
{
    private static AiPrompt Prompt(AiTaskKind kind, string title, string? desc = null, params string[] context)
        => new(kind, title, desc, context);

    [Fact]
    public void SummarizeComments_joins_context_and_reports_when_empty()
    {
        var withComments = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeComments, "Task", null, "Looks good to me", "One nit: rename the var"));
        withComments.Text.ShouldContain("Looks good to me");
        withComments.Text.ShouldContain("rename the var");

        var noComments = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeComments, "Task"));
        noComments.Text.ShouldBe("No comments yet.");
    }

    [Fact]
    public void SummarizeChat_joins_context_and_reports_when_empty()
    {
        var withMessages = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeChat, "general", null, "shipping today", "great work team"));
        withMessages.Text.ShouldContain("shipping today");

        var noMessages = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeChat, "general"));
        noMessages.Text.ShouldBe("No recent messages.");
    }

    [Fact]
    public void SummarizeDocument_leads_with_the_first_sentences_of_the_content()
    {
        var result = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeDocument, "Runbook", "Restart the service. Then check logs."));
        result.Text.ShouldContain("Restart the service.");

        var empty = ExtractiveAi.Complete(Prompt(AiTaskKind.SummarizeDocument, "Runbook"));
        empty.Text.ShouldContain("no content yet");
    }

    [Theory]
    [InlineData(new[] { "overdue" }, "AtRisk")]
    [InlineData(new[] { "blocked" }, "AtRisk")]
    [InlineData(new[] { "due-soon" }, "AtRisk")]
    [InlineData(new string[0], "OnTrack")]
    public void RiskDetect_reads_deterministic_signals(string[] signals, string expectedStatus)
    {
        var result = ExtractiveAi.Complete(Prompt(AiTaskKind.RiskDetect, "Task", null, signals));
        result.Text.Split('|')[0].ShouldBe(expectedStatus);
    }

    [Fact]
    public void WorkspaceQuestionAnswering_answers_only_from_the_supplied_context_never_inventing_content()
    {
        var withContext = ExtractiveAi.Complete(
            Prompt(AiTaskKind.WorkspaceQna, "What is the launch date?", null, "[1] Task: Launch prep — due Friday"));
        withContext.Text.ShouldContain("Launch prep");
        withContext.Text.ShouldContain("Closest matches");

        // No context (e.g. the requester's own search would have returned nothing) => an honest "not found",
        // never a fabricated answer.
        var noContext = ExtractiveAi.Complete(Prompt(AiTaskKind.WorkspaceQna, "What is the launch date?"));
        noContext.Text.ShouldBe("I could not find anything about that in the material I have access to.");
    }
}

public sealed class RedactorTests
{
    [Fact]
    public void Redacts_an_email_address()
    {
        var result = Redactor.Redact("Contact jane.doe@example.com for details.", RedactionOptions.Default);
        result.Text.ShouldNotContain("jane.doe@example.com");
        result.Text.ShouldContain("[REDACTED_EMAIL]");
        result.RedactedCount.ShouldBe(1);
        result.RedactedTypes.ShouldContain("email");
    }

    [Fact]
    public void Redacts_an_api_key_shaped_token()
    {
        var result = Redactor.Redact("Use key sk-proj-abcdefghijklmnopqrstuvwx to authenticate.", RedactionOptions.Default);
        result.Text.ShouldNotContain("sk-proj-abcdefghijklmnopqrstuvwx");
        result.RedactedCount.ShouldBeGreaterThan(0);
        result.RedactedTypes.ShouldContain("api_key");
    }

    [Fact]
    public void Redacts_a_credit_card_shaped_number()
    {
        var result = Redactor.Redact("Card on file: 4111 1111 1111 1111.", RedactionOptions.Default);
        result.Text.ShouldNotContain("4111 1111 1111 1111");
        result.RedactedTypes.ShouldContain("credit_card");
    }

    [Fact]
    public void Respects_disabled_toggles()
    {
        var options = new RedactionOptions(RedactEmails: false, RedactApiKeys: false, RedactCreditCards: false, CustomPatterns: []);
        var result = Redactor.Redact("Email me at a@b.com", options);
        result.Text.ShouldContain("a@b.com");
        result.RedactedCount.ShouldBe(0);
    }

    [Fact]
    public void Applies_a_workspace_custom_pattern()
    {
        var options = new RedactionOptions(false, false, false, ["PROJECT-\\d+"]);
        var result = Redactor.Redact("See ticket PROJECT-4821 for context.", options);
        result.Text.ShouldNotContain("PROJECT-4821");
        result.RedactedTypes.ShouldContain("custom");
    }

    [Fact]
    public void An_invalid_custom_pattern_is_skipped_without_throwing()
    {
        var options = new RedactionOptions(false, false, false, ["(unterminated["]);
        var result = Redactor.Redact("Some text", options);
        result.Text.ShouldBe("Some text");
        result.RedactedCount.ShouldBe(0);
    }

    [Fact]
    public void Empty_or_null_text_is_a_no_op()
    {
        Redactor.Redact(null, RedactionOptions.Default).RedactedCount.ShouldBe(0);
        Redactor.Redact(string.Empty, RedactionOptions.Default).Text.ShouldBe(string.Empty);
    }
}

public sealed class TextSimilarityTests
{
    [Fact]
    public void Identical_titles_score_the_maximum()
        => TextSimilarity.Jaccard("Fix the login bug", "Fix the login bug").ShouldBe(1.0);

    [Fact]
    public void Completely_different_titles_score_zero()
        => TextSimilarity.Jaccard("Fix login bug", "Plan offsite retreat").ShouldBe(0.0);

    [Fact]
    public void Partial_overlap_scores_between_zero_and_one()
    {
        var score = TextSimilarity.Jaccard("Update the billing invoice template", "Update billing invoice logic");
        score.ShouldBeGreaterThan(0.0);
        score.ShouldBeLessThan(1.0);
    }

    [Fact]
    public void Blank_text_never_scores_as_similar()
    {
        TextSimilarity.Jaccard("", "Fix the login bug").ShouldBe(0.0);
        TextSimilarity.Jaccard(null, null).ShouldBe(0.0);
    }

    [Fact]
    public void Is_case_insensitive()
        => TextSimilarity.Jaccard("FIX THE LOGIN BUG", "fix the login bug").ShouldBe(1.0);
}

public sealed class AiProviderSettingsGovernanceTests
{
    private static AiProviderSettings NewSettings()
        => AiProviderSettings.CreateDefault(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Update_rejects_a_model_not_on_the_allow_list()
    {
        var settings = NewSettings();
        settings.UpdateGovernance(["gpt-4*"], true, true, true, [], DateTimeOffset.UtcNow);

        Should.Throw<ValidationAppException>(() =>
            settings.Update("http://localhost:4000", "claude-3", null, true, DateTimeOffset.UtcNow));

        // An allowed (wildcard-matched) model is accepted.
        settings.Update("http://localhost:4000", "gpt-4o-mini", null, true, DateTimeOffset.UtcNow);
        settings.Model.ShouldBe("gpt-4o-mini");
    }

    [Fact]
    public void UpdateGovernance_rejects_an_allow_list_that_would_invalidate_the_current_model()
    {
        var settings = NewSettings();
        settings.Update("http://localhost:4000", "claude-3", null, true, DateTimeOffset.UtcNow);

        Should.Throw<ValidationAppException>(() =>
            settings.UpdateGovernance(["gpt-4*"], true, true, true, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateGovernance_rejects_an_invalid_custom_regex()
    {
        var settings = NewSettings();
        Should.Throw<ValidationAppException>(() =>
            settings.UpdateGovernance([], true, true, true, ["(unterminated["], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Empty_allow_list_permits_any_model()
    {
        var settings = NewSettings();
        settings.Update("http://localhost:4000", "anything-goes", null, true, DateTimeOffset.UtcNow);
        settings.Model.ShouldBe("anything-goes");
    }
}
