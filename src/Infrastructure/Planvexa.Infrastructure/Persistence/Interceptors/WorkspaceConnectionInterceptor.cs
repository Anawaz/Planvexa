namespace Planvexa.Infrastructure.Persistence.Interceptors;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;

/// <summary>
/// Sets the PostgreSQL session variables <c>app.current_workspace</c> and <c>app.current_user</c>
/// whenever a connection is opened so Row-Level Security policies can enforce Workspace isolation as
/// a database-level safety net (defence in depth behind the application query filters). Hardened RLS
/// treats a missing workspace as "no rows"; the user variable powers the narrow bootstrap-read
/// policies (own memberships, own workspaces) that must work before a Workspace is resolved.
/// </summary>
public sealed class WorkspaceConnectionInterceptor(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateApplyCommand(connection);
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = CreateApplyCommand(connection);
        await using (command)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private DbCommand CreateApplyCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT set_config('app.current_user', @user, false), set_config('app.current_workspace', @workspace, false)";

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "user";
        userParameter.Value = CurrentUserValue();
        command.Parameters.Add(userParameter);

        var workspaceParameter = command.CreateParameter();
        workspaceParameter.ParameterName = "workspace";
        workspaceParameter.Value = CurrentWorkspaceValue();
        command.Parameters.Add(workspaceParameter);
        return command;
    }

    private string CurrentWorkspaceValue()
    {
        var context = workspaceAccessor.Current;
        return context.HasWorkspace ? context.WorkspaceId.ToString() : string.Empty;
    }

    private string CurrentUserValue()
        => currentUser.IsAuthenticated ? currentUser.UserId.ToString() : string.Empty;
}
