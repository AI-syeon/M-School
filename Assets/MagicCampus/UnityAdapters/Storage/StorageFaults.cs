using System;

namespace MagicCampus.UnityAdapters.Storage
{
    /// <summary>
    /// Every step boundary of a storage operation. A cancellation request is honoured, and a fault hook runs,
    /// before the work of the named phase. Writes use CreateTemp..PostReplaceVerify, reads use ReadFile/VerifyRead.
    /// </summary>
    public enum StorageFaultPhase
    {
        CreateTemp,
        WriteTemp,
        FlushTemp,
        ReReadTemp,
        VerifyChecksum,
        Backup,
        Replace,
        PostReplaceVerify,
        ReadFile,
        VerifyRead
    }

    /// <summary>Passed to the optional fault hook. The hook may throw (simulated fault), cancel a handle, or touch the files.</summary>
    public sealed class StorageFaultContext
    {
        public StorageFaultPhase Phase { get; }
        public long Generation { get; }
        public string TempPath { get; }    // empty for reads
        public string FinalPath { get; }

        public StorageFaultContext(StorageFaultPhase phase, long generation, string tempPath, string finalPath)
        {
            Phase = phase;
            Generation = generation;
            TempPath = tempPath ?? string.Empty;
            FinalPath = finalPath ?? string.Empty;
        }
    }

    /// <summary>Failure codes local to this adapter; ResultCodes has no I/O code.</summary>
    public static class StorageCodes
    {
        public const string IoFailed = "STORAGE_IO_FAILED", Corrupt = "STORAGE_CORRUPT", NotFound = "STORAGE_NOT_FOUND";
    }
}
