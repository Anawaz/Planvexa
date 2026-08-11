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

    /// <summary>Completed story points per sprint across the last N completed sprints (rolling
    /// velocity), plus the trailing average. See WidgetComputer.VelocityAsync.</summary>
    Velocity = 11,

    /// <summary>Open task count grouped by assignee. See WidgetComputer.TasksByAssigneeAsync.</summary>
    TasksByAssignee = 12,

    /// <summary>Task count grouped by priority. See WidgetComputer.TasksByPriorityAsync.</summary>
    TasksByPriority = 13,

    /// <summary>Tasks created vs. tasks completed within the reporting date range. See
    /// WidgetComputer.CreatedVsCompletedAsync.</summary>
    CreatedVsCompleted = 14,

    /// <summary>Percent-complete per Goal (OKR) in the workspace. See WidgetComputer.GoalProgressAsync.</summary>
    GoalProgress = 15,

    /// <summary>Task count grouped by a chosen WorkManagement custom field's values (configJson:
    /// {"customFieldId": "..."}). See WidgetComputer.CustomFieldBreakdownAsync.</summary>
    CustomFieldBreakdown = 16,
}

/// <summary>Health status a Portfolio owner sets manually (not computed) to communicate at-a-glance status.</summary>
public enum PortfolioStatus
{
    OnTrack = 0,
    AtRisk = 1,
    OffTrack = 2,
}
