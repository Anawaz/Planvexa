namespace Planvexa.Modules.TimeTracking;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.TimeTracking.Application.Services;

public static class TimeTrackingModule
{
    public const string Schema = "time";

    public static IServiceCollection AddTimeTrackingModule(this IServiceCollection services)
    {
        services.AddScoped<TimeServiceContext>();
        services.AddScoped<TimeEntryService>();
        services.AddScoped<TimePolicyService>();
        services.AddScoped<TimesheetService>();
        services.AddScoped<TimeReportService>();
        services.AddScoped<TimeTagService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<MissingTimeReminderRunner>();
        return services;
    }
}
