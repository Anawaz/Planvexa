namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

/// <summary>My Work personal sort/organize preferences (product spec section 15) — global per-user row,
/// see MyWorkPreference's doc comment for why it is not IWorkspaceOwned.</summary>
public sealed class MyWorkPreferenceTests
{
    [Fact]
    public void Create_accepts_a_valid_sort_and_hidden_sections()
    {
        var pref = MyWorkPreference.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), MyWorkPreference.SortByPriority,
            [MyWorkPreference.SectionWatching], DateTimeOffset.UtcNow);

        pref.SortBy.ShouldBe(MyWorkPreference.SortByPriority);
        pref.HiddenSections.ShouldBe([MyWorkPreference.SectionWatching]);
    }

    [Fact]
    public void Create_rejects_an_unknown_sort_value()
    {
        Should.Throw<ArgumentException>(() =>
            MyWorkPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "not-a-sort", [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_rejects_an_unknown_section()
    {
        Should.Throw<ArgumentException>(() =>
            MyWorkPreference.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), MyWorkPreference.SortByDueDate, ["not-a-section"], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_deduplicates_repeated_sections()
    {
        var pref = MyWorkPreference.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), MyWorkPreference.SortByDueDate,
            [MyWorkPreference.SectionCreated, MyWorkPreference.SectionCreated], DateTimeOffset.UtcNow);

        pref.HiddenSections.ShouldBe([MyWorkPreference.SectionCreated]);
    }

    [Fact]
    public void Update_replaces_sort_and_hidden_sections()
    {
        var pref = MyWorkPreference.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), MyWorkPreference.SortByDueDate, [], DateTimeOffset.UtcNow);

        var later = DateTimeOffset.UtcNow.AddMinutes(1);
        pref.Update(MyWorkPreference.SortByTitle, [MyWorkPreference.SectionCreated, MyWorkPreference.SectionWatching], later);

        pref.SortBy.ShouldBe(MyWorkPreference.SortByTitle);
        pref.HiddenSections.ShouldBe([MyWorkPreference.SectionCreated, MyWorkPreference.SectionWatching]);
        pref.UpdatedAtUtc.ShouldBe(later);
    }
}
