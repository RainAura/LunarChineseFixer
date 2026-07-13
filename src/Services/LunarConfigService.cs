using System.Diagnostics;
using System.Text;

namespace RainAura.LunarFontFixer.Services;

internal sealed class LunarConfigService
{
    private readonly LunarCachePatchService _cacheService = new();

    public void AddLunarDirectory(string path) => _cacheService.AddRoot(path);

    public Task<ScanResult> ScanAsync() => Task.Run(Scan);

    public ScanResult Scan() =>
        new(_cacheService.Scan(), IsLunarGameRunning(), DateTime.Now);

    public Task<int> PatchLunarCachesAsync(IEnumerable<LunarCacheTarget> targets) =>
        Task.Run(() =>
        {
            EnsureGameClosed();
            return _cacheService.Patch(targets);
        });

    public Task<int> RestoreLunarCachesAsync(IEnumerable<LunarCacheTarget> targets) =>
        Task.Run(() =>
        {
            EnsureGameClosed();
            return _cacheService.Restore(targets);
        });

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

    public static string BuildReport(ScanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Lunar 1.8.9 中文错字修复工具 - 诊断报告");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"游戏运行中：{(result.LunarGameRunning ? "是" : "否")}");
        builder.AppendLine($"整体状态：{(result.IsHealthy ? "正常" : "需要处理")}");
        builder.AppendLine();
        foreach (var target in result.CacheTargets)
            builder.AppendLine($"[{target.DisplayState}] {target.Source}");
        builder.AppendLine();
        builder.AppendLine("修复原理：向 Lunar FontRenderer 注入兼容模式，禁止旧文字显示列表跨绘制复用，并强制同步 Unicode 字体纹理页。");
        builder.AppendLine("作者：RainAura");
        return builder.ToString();
    }

    private static void EnsureGameClosed()
    {
        if (IsLunarGameRunning())
            throw new InvalidOperationException("检测到 Lunar 游戏仍在运行。请先退出 Minecraft 和 Lunar 启动器。");
    }
}
