namespace Planvexa.UnitTests.Identity;

using Planvexa.Modules.Identity.Domain;
using Shouldly;
using Xunit;

public sealed class UserDisplayNameTests
{
    private static User NewUser() =>
        User.Provision(Guid.CreateVersion7(), "subject-1", "user@example.com", "Original Name", DateTimeOffset.UtcNow);

    [Fact]
    public void UpdateDisplayName_trims_and_applies_new_name()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;

        user.UpdateDisplayName("  New Name  ", now);

        user.DisplayName.ShouldBe("New Name");
        user.UpdatedAtUtc.ShouldBe(now);
    }

    [Fact]
    public void UpdateDisplayName_is_noop_when_name_unchanged()
    {
        var user = NewUser();

        user.UpdateDisplayName("Original Name", DateTimeOffset.UtcNow);

        user.UpdatedAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDisplayName_rejects_empty_or_whitespace(string displayName)
    {
        var user = NewUser();

        Should.Throw<ArgumentException>(() => user.UpdateDisplayName(displayName, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateDisplayName_rejects_names_over_200_characters()
    {
        var user = NewUser();
        var tooLong = new string('a', 201);

        Should.Throw<ArgumentException>(() => user.UpdateDisplayName(tooLong, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateDisplayName_accepts_exactly_200_characters()
    {
        var user = NewUser();
        var maxLength = new string('a', 200);

        user.UpdateDisplayName(maxLength, DateTimeOffset.UtcNow);

        user.DisplayName.ShouldBe(maxLength);
    }

    /// <summary>
    /// Regression guard: every authenticated request re-runs SyncProfile with the IdP's claims
    /// (UserContextMiddleware -> UserDirectory.GetOrProvisionAsync), so a self-service rename must
    /// survive that sync rather than being clobbered back to the IdP-supplied name.
    /// </summary>
    [Fact]
    public void SyncProfile_does_not_override_a_user_set_display_name()
    {
        var user = NewUser();
        user.UpdateDisplayName("My Custom Name", DateTimeOffset.UtcNow);

        user.SyncProfile("user@example.com", "Name From IdP", DateTimeOffset.UtcNow);

        user.DisplayName.ShouldBe("My Custom Name");
    }

    [Fact]
    public void SyncProfile_still_updates_email_after_a_custom_display_name_is_set()
    {
        var user = NewUser();
        user.UpdateDisplayName("My Custom Name", DateTimeOffset.UtcNow);

        user.SyncProfile("new-email@example.com", "Name From IdP", DateTimeOffset.UtcNow);

        user.Email.ShouldBe("new-email@example.com");
        user.DisplayName.ShouldBe("My Custom Name");
    }

    [Fact]
    public void SyncProfile_still_updates_display_name_before_any_custom_rename()
    {
        var user = NewUser();

        user.SyncProfile("user@example.com", "Name From IdP", DateTimeOffset.UtcNow);

        user.DisplayName.ShouldBe("Name From IdP");
    }
}
