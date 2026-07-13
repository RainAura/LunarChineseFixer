# Lunar Chinese Fixer

一个用于诊断和修复 Lunar Client 1.8.9 中文字库错位问题的 Windows 图形化工具。

本项目由Codex构建
旨在帮助使用 Lunar Client 1.8.9 的玩家解决中文文字显示错乱问题。

## 功能

- 自动检测 `.lunarclient/offline/multiver/cache` 内的 ZIP、JAR 格式 Lunar 1.8.9 缓存包；
- 兼容点号类名、标准 `.class` 斜杠路径及不同缓存归档名称；
- 找不到目录时，可手动选择包含 `offline` 文件夹的 `.lunarclient` 目录；
- 自动检测 Lunar 游戏是否仍在运行，避免运行中修改文件；
- 一键将 Lunar `FontRenderer` 的核心绘制方法恢复为 1.8.9 即时绘制；
- 同时覆盖菜单、聊天、计分板等不同文字调用路径；
- 进入服务器或单人世界约 5 秒后自动执行一次资源刷新，修正启动阶段的字体页状态；
- 修改前自动备份，写入后自动校验；
- 支持一键恢复原始 Lunar 缓存文件；
- Lunar 更新覆盖缓存后，可重新运行工具再次修复；
- 自包含单文件 EXE，目标电脑无需安装 .NET。

## 使用方法

1. 完全退出 Lunar Client 中运行的 Minecraft；建议同时退出 Lunar 启动器。
2. 运行 `LunarChineseFixer.exe`。
3. 等待自动扫描。如果没有识别到缓存，点击“选择 Lunar 目录”。
4. 选择包含 `offline` 文件夹的 `.lunarclient` 文件夹。
5. 点击“一键修复”，完成后重新启动 Lunar Client 1.8.9。

## 修复原理

Lunar Client 1.8.9 改写了原版文字渲染流程。启动、进入世界和材质包加载的先后顺序可能让 Unicode 字体页与实际绘制状态不一致，因此会把“岛”“僵”等汉字显示成同一纹理位置上的其他字；不同材质包可能出现不同的错字。手动按 `F3+T` 后恢复也属于这一问题的典型表现。

工具只替换 `FontRenderer` 的核心画字方法，保留 Lunar 其余字段、接口和功能，让文字直接使用 1.8.9 即时绘制，不再经过 Lunar 的文字显示列表。进入世界稳定约 5 秒后，补丁会自动调用与 `F3+T` 同源的资源刷新流程，清除启动阶段遗留的错误字体页状态。

兼容模式以正确显示为优先，文字密集场景的性能可能略低于 Lunar 原有字体缓存。首次进入世界会出现一次正常的资源重载画面。工具不会进行 DLL 或进程注入，不会删除材质包，也不会修改 OptiFine 字体设置。

每个修改过的缓存包旁会保留 `bake.zip.LunarChineseFixer-original`。需要回滚时，点击“恢复原始文件”即可；旧版本创建的备份同样可以识别。

## 自行构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## 作者

RainAura & Codex

## 许可证

[MIT](LICENSE)
