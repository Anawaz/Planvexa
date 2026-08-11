namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A single user's sort/organize choices for the My Work view (product spec section 15: "Users should
/// be able to filter and organize My Work without modifying the underlying shared structure"). My Work
/// spans every Workspace a user belongs to (WorkItemService.ListMineAsync's optional workspaceId), so
/// this is deliberately NOT <see cref="IWorkspaceOwned"/> — it is a global, per-user preference (AGENTS.md
/// rule 4's "truly global user preferences" exception), one row per user, the same shape as
/// identity.users: no RLS, protected by the service layer always scoping to the caller's own UserId.
/// </summary>
public sealed class MyWorkPreference : Entity
{
    public const string SortByDueDate = "dueDate";
    public const string SortByPriority = "priority";
    public const string SortByTitle = "title";

    public static readonly IReadOnlyCollection<string> ValidSortValues = [SortByDueDate, SortByPriority, SortByTitle];

    public const string SectionCreated = "created";
    public const string SectionWatching = "watching";

    public static readonly IReadOnlyCollection<string> ValidSections = [SectionCreated, SectionWatching];

    private List<string> _hiddenSections = [];

    private MyWorkPreference()
    {
    }

    private MyWorkPreference(Guid id, Guid userId, string sortBy, IReadOnlyList<string> hiddenSections, DateTimeOffset nowUtc)
        : base(id)
    {
        UserId = userId;
        SortBy = sortBy;
        _hiddenSections = [.. hiddenSections];
        UpdatedAtUtc = nowUtc;
    }

    public Guid UserId { get; private set; }
    public string SortBy { get; private set; } = SortByDueDate;
    public IReadOnlyList<string> HiddenSections => _hiddenSections;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static MyWorkPreference Create(Guid id, Guid userId, string sortBy, IReadOnlyList<string> hiddenSections, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(userId, nameof(userId));
        return new MyWorkPreference(id, userId, Normalize(sortBy), NormalizeSections(hiddenSections), nowUtc);
    }

    public void Update(string sortBy, IReadOnlyList<string> hiddenSections, DateTimeOffset nowUtc)
    {
        SortBy = Normalize(sortBy);
        _hiddenSections = [.. NormalizeSections(hiddenSections)];
        UpdatedAtUtc = nowUtc;
    }

    private static string Normalize(string sortBy)
    {
        if (!ValidSortValues.Contains(sortBy))
        {
            throw new ArgumentException($"sortBy must be one of: {string.Join(", ", ValidSortValues)}.", nameof(sortBy));
        }

        return sortBy;
    }

    private static IReadOnlyList<string> NormalizeSections(IReadOnlyList<string> hiddenSections)
    {
        var distinct = hiddenSections.Distinct().ToList();
        var invalid = distinct.Where(s => !ValidSections.Contains(s)).ToList();
        if (invalid.Count > 0)
        {
            throw new ArgumentException($"Unknown section(s): {string.Join(", ", invalid)}. Valid: {string.Join(", ", ValidSections)}.");
        }

        return distinct;
    }
}
