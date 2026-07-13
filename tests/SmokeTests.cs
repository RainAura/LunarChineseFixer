using RainAura.LunarFontFixer;
using RainAura.LunarFontFixer.Services;
var testBase = Environment.GetEnvironmentVariable("RAINAURA_TEST_ROOT") ?? Path.GetTempPath();
var root = Path.Combine(testBase, "RainAura-LunarFixer-SmokeTests");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);

try
{
    var lunarBackup = Environment.GetEnvironmentVariable("RAINAURA_LUNAR_BACKUP");
    if (string.IsNullOrWhiteSpace(lunarBackup) || !File.Exists(lunarBackup))
    {
        Console.WriteLine("SKIP: 未提供 RAINAURA_LUNAR_BACKUP，跳过真实 Lunar 缓存测试。");
        return 0;
    }

    var lunarRoot = Path.Combine(root, ".lunarclient");
    var cacheDirectory = Path.Combine(lunarRoot, "offline", "multiver", "cache", "test-build");
    Directory.CreateDirectory(cacheDirectory);
    var testArchive = Path.Combine(cacheDirectory, "bake.zip");
    File.Copy(lunarBackup, testArchive);

    var cacheService = new LunarCachePatchService();
    cacheService.AddRoot(lunarRoot);
    var before = cacheService.Scan().Single(x => x.Path == testArchive);
    Assert(before.State == LunarCacheState.NeedsRepair, "原始 Lunar 缓存应显示需要修复");
    Assert(cacheService.Patch(new[] { before }) == 1, "应成功修复一个 Lunar 缓存包");
    var after = cacheService.Scan().Single(x => x.Path == testArchive);
    Assert(after.State == LunarCacheState.Repaired, "修复后应通过字节码复检");
    Assert(File.Exists(testArchive + LunarCachePatchService.BackupSuffix), "修复前必须创建原始备份");
    Assert(cacheService.Restore(new[] { after }) == 1, "应能恢复原始 Lunar 缓存包");
    var rollback = cacheService.Scan().Single(x => x.Path == testArchive);
    Assert(rollback.State == LunarCacheState.NeedsRepair, "恢复后应回到未修复状态");

    Console.WriteLine("PASS: Lunar 缓存扫描、补丁、校验、备份、恢复全部通过。");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}
