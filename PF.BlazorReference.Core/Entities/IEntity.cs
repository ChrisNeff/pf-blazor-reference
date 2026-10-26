namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// An entity with a generated identity. The setter stays private on the implementer: the factory
/// assigns the id, and EF Core sets it when loading.
/// </summary>
public interface IEntity
{
    Guid Id { get; }
}
