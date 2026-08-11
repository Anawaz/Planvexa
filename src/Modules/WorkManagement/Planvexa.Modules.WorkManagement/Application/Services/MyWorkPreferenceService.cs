namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// My Work personal sort/organize preferences (product spec section 15). Deliberately not built on
/// <see cref="WorkServiceBase"/>: every other WorkManagement service is anchored to one Workspace via
/// <see cref="WorkServiceBase.RequireWorkspace"/>, but My Work spans every Workspace the caller belongs
/// to, so this preference is global to the user (see MyWorkPreference's doc comment) — same self-service,
/// caller-can-only-touch-their-own-row shape as Identity's UserDataService.
/// </summary>
public sealed class MyWorkPreferenceService(
    IMyWorkPreferenceStore store, ICurrentUser currentUser, IIdGenerator ids, IClock clock,
    IAuditWriter audit, IUnitOfWork unitOfWork)
{
    private static readonly MyWorkPreferenceDto Default = new(MyWorkPreference.SortByDueDate, []);

    public async Task<MyWorkPreferenceDto> GetAsync(CancellationToken ct = default)
    {
        var preference = await store.FindAsync(currentUser.UserId, ct);
        return preference is null ? Default : WorkMapper.ToDto(preference);
    }

    public async Task<MyWorkPreferenceDto> SaveAsync(SaveMyWorkPreferenceCommand command, CancellationToken ct = default)
    {
        var existing = await store.FindAsync(currentUser.UserId, ct);
        if (existing is null)
        {
            existing = MyWorkPreference.Create(ids.NewId(), currentUser.UserId, command.SortBy, command.HiddenSections, clock.UtcNow);
            store.Add(existing);
        }
        else
        {
            existing.Update(command.SortBy, command.HiddenSections, clock.UtcNow);
        }

        audit.Write("my_work_preferences.updated", "MyWorkPreference", existing.Id, new { command.SortBy, command.HiddenSections });
        await unitOfWork.SaveChangesAsync(ct);
        return WorkMapper.ToDto(existing);
    }
}
