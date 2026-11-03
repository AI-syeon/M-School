namespace MagicCampus.Contracts
{
    /// <summary>Called only after the runner holds all event resources and durably records scene entry.
    /// Current staging must not warp actors. Reserved staging may join actors under a hidden transition.
    /// Cancellation finishes child work and removes the hidden transition before completing.</summary>
    public interface IEventStagingPort
    {
        IHandle Stage(CompiledEvent definition, Lease lease);
    }
}
