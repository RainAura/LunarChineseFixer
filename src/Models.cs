namespace RainAura.LunarFontFixer;

internal enum FontSettingState
{
    Missing,
    Enabled,
    Disabled,
    Unknown
}

internal sealed class ConfigTarget
{
    public ConfigTarget(string path, string source, FontSettingState state)
    {
        Path = path;
        Source = source;
        State = state;
    }

    public string Path { get; }
    public string Source { get; }
    public FontSettingState State { get; }

    public string DisplayState => State switch
    {
        FontSettingState.Enabled => "需要修复",
        FontSettingState.Disabled => "已修复",
        FontSettingState.Missing => "文件不存在",
        _ => "未检测到设置"
    };
}

internal sealed class ScanResult
{
    public ScanResult(IReadOnlyList<ConfigTarget> targets, bool lunarGameRunning, DateTime scannedAt)
    {
        Targets = targets;
        LunarGameRunning = lunarGameRunning;
        ScannedAt = scannedAt;
    }

    public IReadOnlyList<ConfigTarget> Targets { get; }
    public bool LunarGameRunning { get; }
    public DateTime ScannedAt { get; }

    public int ExistingCount => Targets.Count(x => x.State != FontSettingState.Missing);
    public int EnabledCount => Targets.Count(x => x.State == FontSettingState.Enabled);
    public bool IsHealthy => ExistingCount > 0 && EnabledCount == 0;
}
