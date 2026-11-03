namespace MagicCampus.Contracts
{
    /// <summary>Resumes normal safe-boundary tracking after load or an aborted scene, without publishing scene success.</summary>
    public interface ISafeBoundaryRenewal : ISafeBoundaryPolicy
    {
        void Renew(SafeBoundary boundary, StateSnapshot snapshot, bool saved);
    }
}
