namespace RainAura.LunarFontFixer;

internal enum LunarCacheState
{
    NeedsRepair,
    Repaired,
    Unsupported,
    Unreadable
}

internal sealed class LunarCacheTarget
{
    public LunarCacheTarget(string path, string source, LunarCacheState state, bool backupExists)
    {
        Path = path;
        Source = source;
        State = state;
        BackupExists = backupExists;
    }

    public string Path { get; }
    public string Source { get; }
    public LunarCacheState State { get; }
    public bool BackupExists { get; }

    public string DisplayState => State switch
    {
        LunarCacheState.NeedsRepair => "需要修复",
        LunarCacheState.Repaired => "已修复",
        LunarCacheState.Unsupported => "版本不匹配",
        _ => "读取失败"
    };
}

internal sealed class ScanResult
{
    public ScanResult(IReadOnlyList<LunarCacheTarget> cacheTargets, bool lunarGameRunning, DateTime scannedAt)
    {
        CacheTargets = cacheTargets;
        LunarGameRunning = lunarGameRunning;
        ScannedAt = scannedAt;
    }

    public IReadOnlyList<LunarCacheTarget> CacheTargets { get; }
    public bool LunarGameRunning { get; }
    public DateTime ScannedAt { get; }

    public int CacheCount => CacheTargets.Count(x => x.State is LunarCacheState.NeedsRepair or LunarCacheState.Repaired);
    public int UnpatchedCacheCount => CacheTargets.Count(x => x.State == LunarCacheState.NeedsRepair);
    public int RepairedCacheCount => CacheTargets.Count(x => x.State == LunarCacheState.Repaired);
    public bool IsHealthy => CacheCount > 0 && UnpatchedCacheCount == 0;
}
