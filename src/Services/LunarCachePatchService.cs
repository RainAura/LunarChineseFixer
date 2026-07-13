using System.IO.Compression;

namespace RainAura.LunarFontFixer.Services;

internal sealed class LunarCachePatchService
{
    internal const string FontRendererEntry = "net.minecraft.client.gui.FontRenderer";
    internal const string BackupSuffix = ".LunarChineseFixer-original";
    private const string LegacyBackupSuffix = ".RainAura-original";
    private readonly HashSet<string> _manualRoots = new(StringComparer.OrdinalIgnoreCase);

    public void AddRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path.Trim());
        if (Directory.Exists(Path.Combine(fullPath, "offline"))) _manualRoots.Add(fullPath);
        else if (Directory.Exists(Path.Combine(fullPath, ".lunarclient", "offline")))
            _manualRoots.Add(Path.Combine(fullPath, ".lunarclient"));
        else _manualRoots.Add(fullPath);
    }

    public IReadOnlyList<LunarCacheTarget> Scan()
    {
        var roots = new HashSet<string>(_manualRoots, StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lunarclient")
        };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var cache = Path.Combine(root, "offline", "multiver", "cache");
            try
            {
                if (!Directory.Exists(cache)) continue;
                foreach (var path in Directory.EnumerateFiles(cache, "bake.zip", SearchOption.AllDirectories))
                    paths.Add(path);
            }
            catch
            {
                // 单个目录无权限时继续扫描其他目录。
            }
        }

        return paths.Select(InspectArchive)
            .Where(x => x is not null)
            .Cast<LunarCacheTarget>()
            .OrderBy(x => x.State == LunarCacheState.Repaired ? 1 : 0)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int Patch(IEnumerable<LunarCacheTarget> targets)
    {
        var count = 0;
        foreach (var target in targets.Where(x => x.State == LunarCacheState.NeedsRepair))
        {
            PatchArchive(target.Path);
            count++;
        }
        return count;
    }

    public int Restore(IEnumerable<LunarCacheTarget> targets)
    {
        var count = 0;
        foreach (var target in targets)
        {
            var backup = FindBackup(target.Path);
            if (backup is null) continue;
            ReplaceFromFile(backup, target.Path);
            count++;
        }
        return count;
    }

    private static LunarCacheTarget? InspectArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry(FontRendererEntry);
            if (entry is null) return null;
            var data = ReadEntry(entry);
            var state = JavaClassPatcher.Inspect(data) switch
            {
                JavaPatchState.Patchable => LunarCacheState.NeedsRepair,
                JavaPatchState.AlreadyPatched => LunarCacheState.Repaired,
                _ => LunarCacheState.Unsupported
            };
            var cacheId = Directory.GetParent(path)?.Name ?? "未知版本";
            return new LunarCacheTarget(path, $"Lunar 1.8.9 缓存 · {cacheId}", state,
                FindBackup(path) is not null);
        }
        catch
        {
            return new LunarCacheTarget(path, "Lunar 缓存（读取失败）", LunarCacheState.Unreadable,
                FindBackup(path) is not null);
        }
    }

    private static void PatchArchive(string path)
    {
        byte[] originalClass;
        DateTimeOffset timestamp;
        using (var archive = ZipFile.OpenRead(path))
        {
            var entry = archive.GetEntry(FontRendererEntry) ??
                throw new InvalidDataException("缓存包中缺少 FontRenderer。");
            timestamp = entry.LastWriteTime;
            originalClass = ReadEntry(entry);
        }

        if (!JavaClassPatcher.TryPatch(originalClass, out var patchedClass, out var state) ||
            state == JavaPatchState.Unsupported)
            throw new InvalidDataException("该 Lunar 缓存版本不支持自动修复。");
        if (state == JavaPatchState.AlreadyPatched) return;

        var backup = FindBackup(path) ?? path + BackupSuffix;
        if (!File.Exists(backup)) File.Copy(path, backup, false);

        var temporary = path + ".RainAura-patching";
        File.Copy(path, temporary, true);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                var oldEntry = archive.GetEntry(FontRendererEntry) ??
                    throw new InvalidDataException("缓存包中缺少 FontRenderer。");
                oldEntry.Delete();
                var newEntry = archive.CreateEntry(FontRendererEntry, CompressionLevel.Optimal);
                newEntry.LastWriteTime = timestamp;
                using var stream = newEntry.Open();
                stream.Write(patchedClass, 0, patchedClass.Length);
            }
            ReplaceFromFile(temporary, path, false);

            using var checkArchive = ZipFile.OpenRead(path);
            var checkEntry = checkArchive.GetEntry(FontRendererEntry) ??
                throw new InvalidDataException("写入后未找到 FontRenderer。");
            if (JavaClassPatcher.Inspect(ReadEntry(checkEntry)) != JavaPatchState.AlreadyPatched)
                throw new InvalidDataException("Lunar 缓存补丁写入校验失败。");
        }
        catch
        {
            if (File.Exists(backup)) File.Copy(backup, path, true);
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? FindBackup(string path)
    {
        if (File.Exists(path + BackupSuffix)) return path + BackupSuffix;
        if (File.Exists(path + LegacyBackupSuffix)) return path + LegacyBackupSuffix;
        return null;
    }

    private static void ReplaceFromFile(string source, string destination, bool copySource = true)
    {
        var temporary = destination + ".RainAura-replacing";
        if (copySource) File.Copy(source, temporary, true);
        else if (!string.Equals(source, temporary, StringComparison.OrdinalIgnoreCase))
            File.Move(source, temporary);
        var rollback = destination + ".RainAura-swap";
        try
        {
            if (File.Exists(rollback)) File.Delete(rollback);
            File.Replace(temporary, destination, rollback, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(rollback)) File.Delete(rollback);
        }
    }
}
