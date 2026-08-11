namespace Planvexa.BuildingBlocks.Domain;

/// <summary>Unit of work abstraction so modules can persist without referencing a concrete DbContext.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
