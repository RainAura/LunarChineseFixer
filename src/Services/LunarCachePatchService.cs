using System.IO.Compression;

namespace RainAura.LunarFontFixer.Services;

internal sealed class LunarCachePatchService
{
    internal const string FontRendererEntry = "net.minecraft.client.gui.FontRenderer";
    internal const string FontRendererClassEntry = "net/minecraft/client/gui/FontRenderer.class";
    internal const string BackupSuffix = ".LunarChineseFixer-original";
    private const string LegacyBackupSuffix = ".RainAura-original";
    private readonly HashSet<string> _manualRoots = new(StringComparer.OrdinalIgnoreCase);

    public void AddRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path.Trim());
        var root = ResolveLunarRoot(fullPath);
        _manualRoots.Add(root);
    }

    public IReadOnlyList<LunarCacheTarget> Scan()
    {
        var roots = new HashSet<string>(_manualRoots, StringComparer.OrdinalIgnoreCase);
        AddDefaultRoots(roots);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var cache = Path.Combine(root, "offline", "multiver", "cache");
            try
            {
                if (!Directory.Exists(cache)) continue;
                foreach (var path in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(path);
                    if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);
                }
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
            var entries = FindFontRendererEntries(archive).ToList();
            if (entries.Count == 0) return null;

            var fontStates = entries.Select(x => JavaClassPatcher.Inspect(ReadEntry(x))).ToList();
            var state = fontStates.Any(x => x == JavaPatchState.Patchable)
                ? LunarCacheState.NeedsRepair
                : fontStates.All(x => x == JavaPatchState.AlreadyPatched)
                    ? LunarCacheState.Repaired
                    : LunarCacheState.Unsupported;
            var cacheId = Directory.GetParent(path)?.Name ?? "未知版本";
            var archiveName = Path.GetFileName(path);
            return new LunarCacheTarget(path, $"Lunar 1.8.9 缓存 · {cacheId} · {archiveName}", state,
                FindBackup(path) is not null);
        }
        catch
        {
            return new LunarCacheTarget(path, $"Lunar 缓存（读取失败）· {Path.GetFileName(path)}",
                LunarCacheState.Unreadable, FindBackup(path) is not null);
        }
    }

    private static void PatchArchive(string path)
    {
        var replacements = new List<EntryReplacement>();
        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in FindFontRendererEntries(archive))
            {
                var originalClass = ReadEntry(entry);
                var state = JavaClassPatcher.Inspect(originalClass);
                if (state == JavaPatchState.Patchable)
                {
                    var patchedClass = JavaRuntimeFontPatcher.Transform(originalClass, path);
                    replacements.Add(new EntryReplacement(entry.FullName, entry.LastWriteTime,
                        entry.ExternalAttributes, patchedClass));
                }
            }
        }

        if (replacements.Count == 0)
            throw new InvalidDataException("该 Lunar 缓存包中没有可自动修复的 FontRenderer 类。");

        var backup = FindBackup(path) ?? path + BackupSuffix;
        if (!File.Exists(backup)) File.Copy(path, backup, false);

        var temporary = path + ".RainAura-patching";
        File.Copy(path, temporary, true);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                foreach (var replacement in replacements)
                {
                    var oldEntry = archive.GetEntry(replacement.Name) ??
                        throw new InvalidDataException($"缓存包中缺少 {replacement.Name}。");
                    oldEntry.Delete();
                    var newEntry = archive.CreateEntry(replacement.Name, CompressionLevel.Optimal);
                    newEntry.LastWriteTime = replacement.Timestamp;
                    newEntry.ExternalAttributes = replacement.ExternalAttributes;
                    using var stream = newEntry.Open();
                    stream.Write(replacement.Data, 0, replacement.Data.Length);
                }
            }
            ReplaceFromFile(temporary, path, false);

            using var checkArchive = ZipFile.OpenRead(path);
            foreach (var replacement in replacements)
            {
                var checkEntry = checkArchive.GetEntry(replacement.Name) ??
                    throw new InvalidDataException($"写入后未找到 {replacement.Name}。");
                var verifyState = JavaClassPatcher.Inspect(ReadEntry(checkEntry));
                if (verifyState != JavaPatchState.AlreadyPatched)
                    throw new InvalidDataException($"{replacement.Name} 补丁写入校验失败。");
            }
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

    private static IEnumerable<ZipArchiveEntry> FindFontRendererEntries(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (name.Equals(FontRendererEntry, StringComparison.Ordinal) ||
                name.Equals(FontRendererEntry + ".class", StringComparison.Ordinal) ||
                name.Equals(FontRendererClassEntry, StringComparison.Ordinal) ||
                name.EndsWith("/" + FontRendererClassEntry, StringComparison.Ordinal))
                yield return entry;
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        return memory.ToArray();
    }

    private static void AddDefaultRoots(ISet<string> roots)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) roots.Add(Path.Combine(profile, ".lunarclient"));

        foreach (var variable in new[] { "LUNAR_HOME", "LUNARCLIENT_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) roots.Add(ResolveLunarRoot(Path.GetFullPath(value)));
        }
    }

    private static string ResolveLunarRoot(string path)
    {
        if (Directory.Exists(Path.Combine(path, "offline"))) return path;
        if (Directory.Exists(Path.Combine(path, ".lunarclient", "offline")))
            return Path.Combine(path, ".lunarclient");

        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (current.Name.Equals(".lunarclient", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(current.FullName, "offline")))
                return current.FullName;
            current = current.Parent;
        }
        return path;
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

    private sealed class EntryReplacement
    {
        public EntryReplacement(string name, DateTimeOffset timestamp, int externalAttributes, byte[] data)
        {
            Name = name;
            Timestamp = timestamp;
            ExternalAttributes = externalAttributes;
            Data = data;
        }

        public string Name { get; }
        public DateTimeOffset Timestamp { get; }
        public int ExternalAttributes { get; }
        public byte[] Data { get; }
    }
}
