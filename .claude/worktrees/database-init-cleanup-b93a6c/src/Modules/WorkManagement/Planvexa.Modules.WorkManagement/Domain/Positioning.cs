namespace Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Fractional positioning for drag-and-drop ordering. New items append at the end; a move computes
/// the midpoint between neighbours. Positions are rebalanced by callers only if they get too dense.
/// </summary>
public static class Positioning
{
    public const double Step = 1024d;

    /// <summary>Position for appending after the current maximum (or the first item).</summary>
    public static double Append(double? currentMax) => (currentMax ?? 0d) + Step;

    /// <summary>Midpoint between two neighbours. Null before => before first; null after => after last.</summary>
    public static double Between(double? before, double? after) => (before, after) switch
    {
        (null, null) => Step,
        (null, { } a) => a - Step,
        ({ } b, null) => b + Step,
        ({ } b, { } a) => (b + a) / 2d,
    };
}
