using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RainAura.LunarFontFixer.Services;

internal sealed class LunarConfigService
{
    private static readonly Regex SettingRegex = new(
        @"^ofCustomFonts:(true|false)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HashSet<string> _manualGameDirectories = new(StringComparer.OrdinalIgnoreCase);

    public void AddGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _manualGameDirectories.Add(Path.GetFullPath(path.Trim()));
    }

    public Task<ScanResult> ScanAsync() => Task.Run(Scan);

    public ScanResult Scan()
    {
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var lunarRoot = Path.Combine(userProfile, ".lunarclient");

        AddCandidate(candidates,
            Path.Combine(lunarRoot, "profiles", "1.8", "optionsof.txt"),
            "Lunar 1.8 隔离配置");

        AddCandidate(candidates,
            Path.Combine(appData, ".minecraft", "optionsof.txt"),
            "系统默认 .minecraft");

        foreach (var gameDir in _manualGameDirectories)
            AddCandidate(candidates, Path.Combine(gameDir, "optionsof.txt"), "手动添加目录");

        DiscoverGameDirectoriesFromLogs(lunarRoot, candidates);

        var targets = candidates
            .Select(pair => Inspect(pair.Key, pair.Value))
            .OrderByDescending(x => x.State == FontSettingState.Enabled)
            .ThenByDescending(x => x.State == FontSettingState.Disabled)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ScanResult(targets, IsLunarGameRunning(), DateTime.Now);
    }

    public async Task<int> ApplyAsync(IEnumerable<ConfigTarget> targets, bool enableCustomFonts)
    {
        return await Task.Run(() =>
        {
            if (IsLunarGameRunning())
                throw new InvalidOperationException("检测到 Lunar 游戏仍在运行。请先退出 Minecraft，启动器可以保留。");

            var count = 0;
            foreach (var target in targets.Where(x => File.Exists(x.Path)))
            {
                SetCustomFonts(target.Path, enableCustomFonts);
                count++;
            }
            return count;
        });
    }

    public static bool IsLunarGameRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase) &&
                    !process.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase))
                    continue;

                var executable = process.MainModule?.FileName ?? string.Empty;
                if (executable.IndexOf(".lunarclient", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
                // 某些受保护进程不可读取，忽略即可。
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    public static void SetCustomFonts(string filePath, bool enabled)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var desired = $"ofCustomFonts:{enabled.ToString().ToLowerInvariant()}";
        string updated;

        if (SettingRegex.IsMatch(content))
            updated = SettingRegex.Replace(content, desired);
        else
            updated = content.TrimEnd('\r', '\n') + Environment.NewLine + desired + Environment.NewLine;

        if (!string.Equals(content, updated, StringComparison.Ordinal))
            File.WriteAllText(filePath, updated, new UTF8Encoding(false));
    }

    public static string BuildReport(ScanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("RainAura Lunar 1.8.9 中文错字修复工具 - 诊断报告");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"游戏运行中：{(result.LunarGameRunning ? "是" : "否")}");
        builder.AppendLine($"整体状态：{(result.IsHealthy ? "正常" : "需要处理")}");
        builder.AppendLine();
        foreach (var target in result.Targets)
            builder.AppendLine($"[{target.DisplayState}] {target.Source}");
        builder.AppendLine();
        builder.AppendLine("修复原理：关闭 OptiFine Custom Fonts，绕过 mcpatcher/font 旧式 Unicode 字库页错位。");
        builder.AppendLine("作者：RainAura");
        return builder.ToString();
    }

    private static ConfigTarget Inspect(string path, string source)
    {
        if (!File.Exists(path)) return new ConfigTarget(path, source, FontSettingState.Missing);
        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            var match = SettingRegex.Match(content);
            if (!match.Success) return new ConfigTarget(path, source, FontSettingState.Unknown);
            return new ConfigTarget(path, source,
                match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? FontSettingState.Enabled
                    : FontSettingState.Disabled);
        }
        catch
        {
            return new ConfigTarget(path, source, FontSettingState.Unknown);
        }
    }

    private static void DiscoverGameDirectoriesFromLogs(
        string lunarRoot,
        IDictionary<string, string> candidates)
    {
        var logPaths = new[]
        {
            Path.Combine(lunarRoot, "profiles", "1.8", "logs", "ichor-boot.log"),
            Path.Combine(lunarRoot, "profiles", "1.8", "logs", "latest.log")
        };

        var patterns = new[]
        {
            new Regex("--gameDir\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase),
            new Regex("--gameDir\\s+([^\\s]+)", RegexOptions.IgnoreCase)
        };

        foreach (var logPath in logPaths.Where(File.Exists))
        {
            try
            {
                var text = File.ReadAllText(logPath, Encoding.UTF8);
                foreach (var pattern in patterns)
                foreach (Match match in pattern.Matches(text))
                    AddCandidate(candidates,
                        Path.Combine(match.Groups[1].Value, "optionsof.txt"),
                        "Lunar 日志识别目录");
            }
            catch
            {
                // 日志读取失败不影响固定路径扫描。
            }
        }
    }

    private static void AddCandidate(IDictionary<string, string> candidates, string path, string source)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!candidates.ContainsKey(fullPath))
                candidates.Add(fullPath, source);
        }
        catch
        {
            // 忽略无效路径。
        }
    }
}
