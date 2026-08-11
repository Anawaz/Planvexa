namespace Planvexa.Modules.Reporting.Domain;

/// <summary>The kinds of dashboard widget the reporting engine can compute.</summary>
public enum WidgetType
{
    TasksByStatus = 0,
    Overdue = 1,
    Completed = 2,
    TimeLogged = 3,
    BillableTotals = 4,
    Workload = 5,
    EstimateVsActual = 6,
    SprintProgress = 7,
    PortfolioHealth = 8,

    /// <summary>Day-by-day remaining/completed points across a sprint's date range (a real
    /// time series, unlike <see cref="SprintProgress"/>'s single snapshot). See WidgetComputer.BurndownAsync.</summary>
    Burndown = 9,

    /// <summary>A user-defined aggregate formula (e.g. <c>SUM(hours) / COUNT(tasks)</c>) evaluated
    /// over per-Space portfolio rows. See WidgetComputer.CustomFormulaAsync.</summary>
    CustomFormula = 10,
}
