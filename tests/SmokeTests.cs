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
    var alternateArchive = Path.Combine(cacheDirectory, "client-classes.jar");
    CreateAlternateArchive(lunarBackup, alternateArchive);

    var cacheService = new LunarCachePatchService();
    cacheService.AddRoot(lunarRoot);
    var before = cacheService.Scan().Where(x => x.Path.StartsWith(cacheDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
    Assert(before.Count == 2, "应识别 ZIP 和 JAR 两种 Lunar 缓存归档");
    Assert(before.All(x => x.State == LunarCacheState.NeedsRepair), "原始 Lunar 缓存应显示需要修复");
    Assert(cacheService.Patch(before) == 2, "应成功修复两种 Lunar 缓存包");
    var after = cacheService.Scan().Where(x => x.Path.StartsWith(cacheDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
    Assert(after.Count == 2 && after.All(x => x.State == LunarCacheState.Repaired),
        "不同归档名和类条目格式修复后都应通过字节码复检");
    var outputArchive = Environment.GetEnvironmentVariable("RAINAURA_PATCHED_OUTPUT");
    if (!string.IsNullOrWhiteSpace(outputArchive)) File.Copy(testArchive, outputArchive, true);
    Assert(File.Exists(testArchive + LunarCachePatchService.BackupSuffix), "修复前必须创建原始备份");
    Assert(File.Exists(alternateArchive + LunarCachePatchService.BackupSuffix), "JAR 缓存修复前也必须创建原始备份");
    Assert(cacheService.Restore(after) == 2, "应能恢复两种原始 Lunar 缓存包");
    var rollback = cacheService.Scan().Where(x => x.Path.StartsWith(cacheDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
    Assert(rollback.Count == 2 && rollback.All(x => x.State == LunarCacheState.NeedsRepair),
        "恢复后应回到未修复状态");

    Console.WriteLine("PASS: Lunar 缓存扫描、即时绘制替换、进服延迟刷新、校验、备份、恢复全部通过。");
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

static void CreateAlternateArchive(string source, string destination)
{
    File.Copy(source, destination);
    using var archive = System.IO.Compression.ZipFile.Open(destination, System.IO.Compression.ZipArchiveMode.Update);
    var oldEntry = archive.GetEntry(LunarCachePatchService.FontRendererEntry) ??
        throw new InvalidDataException("测试源缓存缺少 FontRenderer。");
    using var memory = new MemoryStream();
    using (var stream = oldEntry.Open()) stream.CopyTo(memory);
    var timestamp = oldEntry.LastWriteTime;
    oldEntry.Delete();
    var newEntry = archive.CreateEntry(LunarCachePatchService.FontRendererClassEntry);
    newEntry.LastWriteTime = timestamp;
    using var output = newEntry.Open();
    var data = memory.ToArray();
    output.Write(data, 0, data.Length);
}
