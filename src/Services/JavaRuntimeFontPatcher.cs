using System.Diagnostics;
using System.Reflection;

namespace RainAura.LunarFontFixer.Services;

internal static class JavaRuntimeFontPatcher
{
    private const string HelperClass = "rainaura.fontpatch.RainAuraFontRendererRestorer";
    private static readonly object Sync = new();
    private static string? _runtimeDirectory;

    public static byte[] Transform(byte[] input, string archivePath)
    {
        var runtime = EnsureRuntimeFiles();
        var java = FindJava(archivePath);
        var work = Path.Combine(runtime, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = Path.Combine(work, "FontRenderer.class");
            var output = Path.Combine(work, "FontRenderer-patched.class");
            File.WriteAllBytes(source, input);
            var classPath = string.Join(Path.PathSeparator.ToString(), new[]
            {
                runtime,
                Path.Combine(runtime, "asm-9.7.jar"),
                Path.Combine(runtime, "asm-tree-9.7.jar")
            });
            var arguments = string.Join(" ", new[]
            {
                "-cp",
                Quote(classPath),
                HelperClass,
                Quote(source),
                Quote(Path.Combine(runtime, "DonorFontRenderer.class")),
                Quote(output)
            });
            var startInfo = new ProcessStartInfo
            {
                FileName = java,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work
            };
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("无法启动 Lunar Java 字体修复器。");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("Lunar Java 字体修复器运行超时。");
            }
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidDataException("字体绘制方法转换失败：" +
                    (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim());

            var result = File.ReadAllBytes(output);
            if (JavaClassPatcher.Inspect(result) != JavaPatchState.AlreadyPatched)
                throw new InvalidDataException("字体绘制方法转换结果未通过标记校验。");
            return result;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static string EnsureRuntimeFiles()
    {
        lock (Sync)
        {
            if (_runtimeDirectory is not null && Directory.Exists(_runtimeDirectory))
                return _runtimeDirectory;
            var directory = Path.Combine(Path.GetTempPath(), "RainAura-LunarChineseFixer", "fontpatch-1.0.12");
            Directory.CreateDirectory(directory);
            Extract("RainAura.FontPatch.Asm", Path.Combine(directory, "asm-9.7.jar"));
            Extract("RainAura.FontPatch.AsmTree", Path.Combine(directory, "asm-tree-9.7.jar"));
            Extract("RainAura.FontPatch.Donor", Path.Combine(directory, "DonorFontRenderer.class"));
            var helperDirectory = Path.Combine(directory, "rainaura", "fontpatch");
            Directory.CreateDirectory(helperDirectory);
            Extract("RainAura.FontPatch.Helper", Path.Combine(helperDirectory, "RainAuraFontRendererRestorer.class"));
            Extract("RainAura.FontPatch.HelperWriter",
                Path.Combine(helperDirectory, "RainAuraFontRendererRestorer$SafeClassWriter.class"));
            _runtimeDirectory = directory;
            return directory;
        }
    }

    private static void Extract(string resourceName, string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException("程序内缺少字体修复资源：" + resourceName);
        using var output = File.Create(destination);
        source.CopyTo(output);
    }

    private static string FindJava(string archivePath)
    {
        var lunarRoot = FindLunarRoot(archivePath);
        if (lunarRoot is not null)
        {
            var jreRoot = Path.Combine(lunarRoot, "jre");
            if (Directory.Exists(jreRoot))
            {
                var candidates = Directory.EnumerateFiles(jreRoot, "java.exe", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(Path.Combine("bin", "java.exe"), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
                if (candidates.Count > 0) return candidates[0];
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return "java.exe";
    }

    private static string? FindLunarRoot(string path)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(path) ?? path);
        while (current is not null)
        {
            if (current.Name.Equals(".lunarclient", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
