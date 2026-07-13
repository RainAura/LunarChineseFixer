# Lunar Chinese Fixer

一个用于诊断和修复 Lunar Client 1.8.9 中文字库错位问题的 Windows 图形化工具。

旨在帮助使用 Lunar Client 1.8.9 的玩家解决中文文字显示错乱问题。

## 功能

- 自动检测 `.lunarclient/offline/multiver/cache` 内的 ZIP、JAR 格式 Lunar 1.8.9 缓存包；
- 兼容点号类名、标准 `.class` 斜杠路径及不同缓存归档名称；
- 找不到目录时，可手动选择包含 `offline` 文件夹的 `.lunarclient` 目录；
- 自动检测 Lunar 游戏是否仍在运行，避免运行中修改文件；
- 一键修补 Lunar 的字体渲染缓存清理流程；
- 向 Lunar `FontRenderer` 注入兼容模式，禁止错误显示列表跨绘制复用；
- 在缓存文字真正绘制前强制同步 Unicode 字体纹理页；
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

Lunar Client 1.8.9 为文字渲染增加了显示列表缓存，但资源或材质包重载时没有清空旧缓存。旧显示列表继续引用先前绑定的 Unicode 字体贴图页，因此可能把“岛”等汉字显示成其他字；不同材质包也可能出现不同的错字。

工具会直接向 Lunar 的 `FontRenderer` 注入兼容模式：每次绘制文字前销毁旧显示列表并现场重建，不再复用可能绑定错误字体页的历史缓存；同时在每个渲染段绘制前使旧的 OpenGL 纹理状态失效，再绑定实际使用的 Unicode 字体页。该方案不依赖具体汉字或材质包，可避免“岛”显示成“涛”这类低字节相同、页码错误的错字。

兼容模式以正确显示为优先，文字密集场景的性能可能略低于 Lunar 原有字体缓存。工具不会进行 DLL 或进程注入，不会删除材质包，也不会修改 OptiFine 字体设置。

每个修改过的缓存包旁会保留 `bake.zip.LunarChineseFixer-original`。需要回滚时，点击“恢复原始文件”即可；旧版本创建的备份同样可以识别。

## 自行构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## 作者

RainAura

## 许可证

[MIT](LICENSE)
