namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.Goals.Application;
using Planvexa.Modules.Goals.Application.Services;
using Planvexa.Modules.Goals.Domain;

// ---- Request models ----
public sealed record CreateGoalRequest(
    Guid? FolderId, string Name, string? Description, Guid? OwnerUserId,
    DateTimeOffset StartDate, DateTimeOffset EndDate, GoalTargetType TargetType,
    decimal? TargetValue, decimal? CurrentValue);

public sealed record UpdateGoalRequest(
    string? Name, string? Description, Guid? FolderId, DateTimeOffset? StartDate, DateTimeOffset? EndDate,
    decimal? CurrentValue, GoalStatus? Status);

public sealed record LinkGoalTaskRequest(Guid TaskId);

public sealed record CreateGoalFolderRequest(string Name);

public sealed record AddGoalCommentRequest(string Body);

public sealed class CreateGoalRequestValidator : AbstractValidator<CreateGoalRequest>
{
    public CreateGoalRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.TargetValue).GreaterThan(0).When(x => x.TargetType == GoalTargetType.Numeric)
            .WithMessage("A numeric-target goal requires a positive target value.");
    }
}

public sealed class CreateGoalFolderRequestValidator : AbstractValidator<CreateGoalFolderRequest>
{
    public CreateGoalFolderRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class AddGoalCommentRequestValidator : AbstractValidator<AddGoalCommentRequest>
{
    public AddGoalCommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}

/// <summary>Goals/OKR endpoints.</summary>
public static class GoalEndpoints
{
    public static void MapGoalEndpoints(this RouteGroupBuilder api)
    {
        MapFolders(api);
        MapGoals(api);
        MapComments(api);
    }

    private static void MapFolders(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goal-folders").RequireAuthorization();

        group.MapGet("/", async (GoalFolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateGoalFolderRequest r, GoalFolderService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(r.Name, ct);
                return Results.Created($"/api/v1/goal-folders/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateGoalFolderRequest>>();

        group.MapPut("/{id:guid}", async (Guid id, CreateGoalFolderRequest r, GoalFolderService svc, CancellationToken ct) =>
                Results.Ok(await svc.RenameAsync(id, r.Name, ct)))
            .AddEndpointFilter<ValidationFilter<CreateGoalFolderRequest>>();

        group.MapDelete("/{id:guid}", async (Guid id, GoalFolderService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static void MapGoals(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goals").RequireAuthorization();

        group.MapGet("/", async (Guid? folderId, GoalService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(folderId, ct)));

        group.MapGet("/{id:guid}", async (Guid id, GoalService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDetailAsync(id, ct)));

        group.MapPost("/", async (CreateGoalRequest r, GoalService svc, CancellationToken ct) =>
            {
                var dto = await svc.CreateAsync(new CreateGoalCommand(
                    r.FolderId, r.Name, r.Description, r.OwnerUserId, r.StartDate, r.EndDate, r.TargetType, r.TargetValue, r.CurrentValue), ct);
                return Results.Created($"/api/v1/goals/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateGoalRequest>>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateGoalRequest r, GoalService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateGoalCommand(
                r.Name, r.Description, r.FolderId, r.StartDate, r.EndDate, r.CurrentValue, r.Status), ct)));

        group.MapDelete("/{id:guid}", async (Guid id, GoalService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/linked-tasks", async (Guid id, LinkGoalTaskRequest r, GoalService svc, CancellationToken ct) =>
            Results.Ok(await svc.LinkTaskAsync(id, r.TaskId, ct)));

        group.MapDelete("/{id:guid}/linked-tasks/{taskId:guid}", async (Guid id, Guid taskId, GoalService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnlinkTaskAsync(id, taskId, ct)));
    }

    private static void MapComments(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goals/{goalId:guid}/comments").RequireAuthorization();

        group.MapGet("/", async (Guid goalId, GoalCommentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(goalId, ct)));

        group.MapPost("/", async (Guid goalId, AddGoalCommentRequest r, GoalCommentService svc, CancellationToken ct) =>
            {
                var dto = await svc.AddAsync(goalId, r.Body, ct);
                return Results.Created($"/api/v1/goals/{goalId}/comments/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<AddGoalCommentRequest>>();
    }
}
