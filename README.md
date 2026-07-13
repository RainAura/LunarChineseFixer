# Lunar Chinese Fixer

一个用于诊断和修复 Lunar Client 1.8.9 中文字库错位问题的 Windows 图形化工具。

本程序完全依赖Codex开发 旨在帮助部分我的世界玩家解决字体问题

##功能

- 自动检测当前用户的 Lunar 1.8 隔离配置；
- 自动检测系统默认 `.minecraft` 目录；
- 尝试从 Lunar 日志识别自定义 `gameDir`；
- 自动检测 Lunar 游戏是否仍在运行，避免退出时覆盖配置；
- 找不到目录时，可通过界面手动选择任意 `.minecraft` 文件夹；
- 一键关闭或恢复 OptiFine `Custom Fonts`；
- 自包含单文件 EXE，目标电脑无需安装 .NET。

## 使用方法

1. 完全退出 Lunar Client 中运行的 Minecraft，Lunar 启动器可以保留。
2. 运行 `LunarChineseFixer.exe`。
3. 等待自动扫描。如果没有识别到目录，点击“选择游戏目录”。
4. 选择包含 `optionsof.txt` 的 `.minecraft` 文件夹。
5. 点击“一键修复”，完成后重新启动 Lunar Client 1.8.9。

## 修复原理

Lunar Client 1.8.9 使用 OptiFine 旧式 `mcpatcher/font` 自定义字体加载路径时，部分环境可能出现 Unicode 字库页错位。工具会将：

```text
ofCustomFonts:true
```

改为：

```text
ofCustomFonts:false
```

从而让客户端回到原版字体加载路径。工具不会删除材质包，也不会修改其他 OptiFine 选项。

## 自行构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## 作者

RainAura & Codex

## 许可证

[MIT](LICENSE)
