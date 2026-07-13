using RainAura.LunarFontFixer;
using RainAura.LunarFontFixer.Services;
using System.Text;

var root = Path.Combine(Path.GetTempPath(), "RainAura-LunarFixer-SmokeTests");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);

try
{
    var options = Path.Combine(root, "optionsof.txt");
    File.WriteAllText(options, "ofFastRender:false\nofCustomFonts:true\nofSmartAnimations:false\n", new UTF8Encoding(false));

    LunarConfigService.SetCustomFonts(options, false);
    var fixedText = File.ReadAllText(options, Encoding.UTF8);
    Assert(fixedText.Contains("ofCustomFonts:false"), "Custom Fonts 应变为 false");
    Assert(fixedText.Contains("ofFastRender:false"), "其他选项必须保留");
    Assert(!fixedText.Contains("ofCustomFonts:true"), "旧设置不应残留");

    var service = new LunarConfigService();
    service.AddGameDirectory(root);
    var scan = service.Scan();
    var target = scan.Targets.FirstOrDefault(x => string.Equals(x.Path, options, StringComparison.OrdinalIgnoreCase));
    Assert(target is not null, "应识别手动添加的目录");
    Assert(target!.State == FontSettingState.Disabled, "扫描结果应为已修复");

    LunarConfigService.SetCustomFonts(options, true);
    var restored = File.ReadAllText(options, Encoding.UTF8);
    Assert(restored.Contains("ofCustomFonts:true"), "恢复功能应变为 true");

    Console.WriteLine("PASS: 配置替换、选项保留、目录扫描、恢复功能全部通过。");
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
