namespace Planvexa.Modules.Goals.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Goals.Authorization;
using Planvexa.Modules.Goals.Domain;

public sealed class GoalFolderService(GoalServiceContext ctx, IGoalFolderStore folders) : GoalServiceBase(ctx)
{
    public async Task<IReadOnlyList<GoalFolderDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureRead(await RoleAsync(workspaceId, ct));
        var list = await folders.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(f => new GoalFolderDto(f.Id, f.Name)).ToList();
    }

    public async Task<GoalFolderDto> CreateAsync(string name, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var folder = GoalFolder.Create(NewId(), workspaceId, name, UserId, Now);
        folders.Add(folder);
        Audit("goals.folder_created", "GoalFolder", folder.Id, new { name });
        await SaveAsync(ct);
        return new GoalFolderDto(folder.Id, folder.Name);
    }

    public async Task<GoalFolderDto> RenameAsync(Guid id, string name, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var folder = await folders.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal folder not found.");
        folder.Rename(name);
        Audit("goals.folder_renamed", "GoalFolder", folder.Id);
        await SaveAsync(ct);
        return new GoalFolderDto(folder.Id, folder.Name);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        var folder = await folders.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Goal folder not found.");
        Audit("goals.folder_deleted", "GoalFolder", folder.Id);
        folders.Remove(folder);
        await SaveAsync(ct);
    }
}

public sealed class GoalCommentService(GoalServiceContext ctx, IGoalCommentStore comments, IGoalStore goals) : GoalServiceBase(ctx)
{
    public async Task<IReadOnlyList<GoalCommentDto>> ListAsync(Guid goalId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureRead(await RoleAsync(workspaceId, ct));
        var list = await comments.ListByGoalAsync(workspaceId, goalId, ct);
        return list.Select(c => new GoalCommentDto(c.Id, c.AuthorUserId, c.Body, c.CreatedAtUtc)).ToList();
    }

    public async Task<GoalCommentDto> AddAsync(Guid goalId, string body, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GoalAuthorizer.EnsureEdit(await RoleAsync(workspaceId, ct));

        _ = await goals.FindAsync(workspaceId, goalId, ct) ?? throw new NotFoundException("Goal not found.");
        var comment = Domain.GoalComment.Create(NewId(), workspaceId, goalId, UserId, body, Now);
        comments.Add(comment);
        Audit("goals.comment_added", "Goal", goalId);
        await SaveAsync(ct);
        return new GoalCommentDto(comment.Id, comment.AuthorUserId, comment.Body, comment.CreatedAtUtc);
    }
}
