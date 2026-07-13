using RainAura.LunarFontFixer.Controls;
using RainAura.LunarFontFixer.Services;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace RainAura.LunarFontFixer;

internal sealed class MainForm : Form
{
    private const string RepositoryUrl = "https://github.com/RainAura/LunarChineseFixer";
    private readonly LunarConfigService _service = new();
    private ScanResult? _lastResult;

    private readonly Label _statusTitle = new();
    private readonly Label _statusDetail = new();
    private readonly Panel _statusBar = new();
    private readonly ListView _targetList = new();
    private readonly RichTextBox _logBox = new();
    private readonly Button _scanButton = new() { Text = "重新扫描" };
    private readonly Button _browseButton = new() { Text = "选择游戏路径" };
    private readonly Button _fixButton = new() { Text = "一键修复" };
    private readonly Button _restoreButton = new() { Text = "恢复字体设置" };
    private readonly Button _reportButton = new() { Text = "导出报告" };
    private readonly IconTextButton _githubButton = new();
    private readonly ToolTip _toolTip = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _directoryHint = new();

    public MainForm()
    {
        Text = "Lunar 1.8.9 中文错字修复工具";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(820, 560);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        DoubleBuffered = true;

        BuildLayout();
        WireEvents();
        Shown += async (_, _) => await ScanAsync("启动时自动扫描");
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.White,
            Padding = new Padding(24, 0, 24, 0)
        };
        var logo = new PictureBox
        {
            Location = new Point(24, 19),
            Size = new Size(44, 44),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap()
        };
        var title = new Label
        {
            Text = "Lunar Chinese Fixer",
            Location = new Point(82, 16),
            Size = new Size(420, 31),
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = Theme.Text
        };
        var subtitle = new Label
        {
            Text = "Lunar Client 1.8.9 中文错字修复  ·  v1.0.7",
            Location = new Point(84, 50),
            Size = new Size(430, 22),
            ForeColor = Theme.Muted
        };
        StyleSecondaryButton(_githubButton);
        _githubButton.Text = "GitHub";
        _githubButton.Image = LoadGitHubImage();
        _githubButton.FlatAppearance.BorderSize = 0;
        _githubButton.ImageAlign = ContentAlignment.MiddleCenter;
        _githubButton.TextAlign = ContentAlignment.MiddleCenter;
        _githubButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _githubButton.Padding = new Padding(0);
        _githubButton.Dock = DockStyle.Right;
        _githubButton.Width = 88;
        _toolTip.SetToolTip(_githubButton, "打开 GitHub 项目仓库");
        var authorLabel = new Label
        {
            Text = "作者  RainAura & Codex",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = Theme.Muted
        };
        var githubHost = new Panel
        {
            Dock = DockStyle.Right,
            Width = 282,
            Padding = new Padding(6, 22, 0, 22),
            BackColor = Color.White
        };
        githubHost.Controls.AddRange([authorLabel, _githubButton]);
        header.Controls.AddRange([logo, title, subtitle, githubHost]);
        Controls.Add(header);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Background
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(body);
        body.BringToFront();

        var statusPanel = CreateSectionPanel();
        statusPanel.Margin = new Padding(0, 0, 0, 14);
        _statusBar.Dock = DockStyle.Left;
        _statusBar.Width = 5;
        _statusBar.BackColor = Theme.Muted;
        _statusTitle.Text = "准备扫描";
        _statusTitle.Location = new Point(24, 12);
        _statusTitle.Size = new Size(560, 25);
        _statusTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _statusTitle.ForeColor = Theme.Text;
        _statusDetail.Text = "正在等待检测 Lunar 1.8 配置";
        _statusDetail.Location = new Point(24, 39);
        _statusDetail.Size = new Size(650, 22);
        _statusDetail.ForeColor = Theme.Muted;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;
        _progress.Visible = false;
        _progress.Size = new Size(110, 5);
        _progress.Location = new Point(630, 29);
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        statusPanel.Controls.AddRange([_statusBar, _statusTitle, _statusDetail, _progress]);
        body.Controls.Add(statusPanel, 0, 0);

        var configPanel = CreateSectionPanel();
        configPanel.Margin = new Padding(0, 0, 0, 14);
        var configHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 66,
            BackColor = Color.White
        };
        var configTitle = SectionTitle("检测到的配置", 18, 12);
        _directoryHint.Text = "请选择 Lunar 客户端的我的世界路径";
        _directoryHint.Location = new Point(18, 39);
        _directoryHint.Size = new Size(480, 22);
        _directoryHint.ForeColor = Theme.Muted;
        StylePrimaryButton(_browseButton);
        _browseButton.Dock = DockStyle.Fill;
        var browseHost = new Panel
        {
            Dock = DockStyle.Right,
            Width = 184,
            Padding = new Padding(10, 13, 18, 13),
            BackColor = Color.White
        };
        browseHost.Controls.Add(_browseButton);
        configHeader.Controls.AddRange([configTitle, _directoryHint, browseHost]);

        _targetList.Dock = DockStyle.Fill;
        _targetList.View = View.Details;
        _targetList.FullRowSelect = true;
        _targetList.GridLines = true;
        _targetList.BorderStyle = BorderStyle.FixedSingle;
        _targetList.BackColor = Color.White;
        _targetList.ForeColor = Theme.Text;
        _targetList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _targetList.Columns.Add("状态", 110);
        _targetList.Columns.Add("配置位置", 585);
        _targetList.Resize += (_, _) =>
            _targetList.Columns[1].Width = Math.Max(220, _targetList.ClientSize.Width - _targetList.Columns[0].Width - 5);
        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 16),
            BackColor = Color.White
        };
        listHost.Controls.Add(_targetList);
        configPanel.Controls.AddRange([listHost, configHeader]);
        body.Controls.Add(configPanel, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Background
        };
        StylePrimaryButton(_fixButton);
        StyleSecondaryButton(_scanButton);
        StyleSecondaryButton(_restoreButton);
        _fixButton.Size = new Size(132, 44);
        _scanButton.Size = new Size(118, 44);
        _restoreButton.Size = new Size(142, 44);
        _fixButton.Margin = new Padding(0, 0, 12, 0);
        _scanButton.Margin = new Padding(0, 0, 12, 0);
        _restoreButton.Margin = new Padding(0);
        actions.Controls.AddRange([_fixButton, _scanButton, _restoreButton]);
        body.Controls.Add(actions, 0, 2);

        var logPanel = CreateSectionPanel();
        logPanel.Margin = new Padding(0);
        var logTitle = SectionTitle("诊断日志", 18, 13);
        StyleSecondaryButton(_reportButton);
        _reportButton.Size = new Size(112, 34);
        _reportButton.Location = new Point(618, 11);
        _reportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _logBox.Location = new Point(18, 54);
        _logBox.Size = new Size(714, 78);
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _logBox.BackColor = Color.FromArgb(248, 249, 250);
        _logBox.ForeColor = Color.FromArgb(72, 76, 82);
        _logBox.Font = new Font("Cascadia Mono", 8.5F);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.ReadOnly = true;
        logPanel.Controls.AddRange([logTitle, _reportButton, _logBox]);
        body.Controls.Add(logPanel, 0, 3);

        ResumeLayout(true);
    }

    private void WireEvents()
    {
        _scanButton.Click += async (_, _) => await ScanAsync("手动重新扫描");
        _browseButton.Click += async (_, _) => await BrowseAsync();
        _fixButton.Click += async (_, _) => await ApplyAsync(false);
        _restoreButton.Click += async (_, _) => await ApplyAsync(true);
        _reportButton.Click += (_, _) => ExportReport();
        _githubButton.Click += (_, _) => OpenRepository();
    }

    private async Task ScanAsync(string reason)
    {
        SetBusy(true, "正在扫描…", "检查 Lunar 隔离配置、默认目录和历史 gameDir");
        Log(reason);
        try
        {
            _lastResult = await _service.ScanAsync();
            RenderResult(_lastResult);
            Log($"扫描完成：找到 {_lastResult.ExistingCount} 个有效配置，{_lastResult.EnabledCount} 个需要修复");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task BrowseAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "请选择 Lunar 客户端的我的世界路径（包含 optionsof.txt 的游戏目录）",
            ShowNewFolderButton = false
        };
#if !NETFRAMEWORK
        dialog.UseDescriptionForTitle = true;
#endif
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _service.AddGameDirectory(dialog.SelectedPath);
        Log("已添加手动选择的 .minecraft 目录");
        await ScanAsync("扫描手动添加的游戏目录");
    }

    private async Task ApplyAsync(bool restore)
    {
        if (_lastResult is null || _lastResult.ExistingCount == 0)
        {
            MessageBox.Show("尚未检测到有效配置，请先选择 Lunar 客户端的我的世界路径。", "RainAura",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (restore)
        {
            var answer = MessageBox.Show(
                "恢复后会重新启用 OptiFine Custom Fonts，中文字库错位可能再次出现。是否继续？",
                "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        SetBusy(true, restore ? "正在恢复…" : "正在修复…", "安全写入 UTF-8 配置文件");
        try
        {
            var changed = await _service.ApplyAsync(_lastResult.Targets, restore);
            Log($"{(restore ? "恢复" : "修复")}完成，共处理 {changed} 个配置文件");
            await ScanAsync("写入后自动校验");
            MessageBox.Show(restore ? "已恢复 Custom Fonts。" : "修复完成，现在可以启动 Lunar Client 1.8.9。",
                "RainAura", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private void RenderResult(ScanResult result)
    {
        _targetList.BeginUpdate();
        _targetList.Items.Clear();
        foreach (var target in result.Targets)
        {
            var item = new ListViewItem(target.DisplayState);
            item.SubItems.Add(target.Source);
            item.ForeColor = target.State switch
            {
                FontSettingState.Enabled => Theme.Warning,
                FontSettingState.Disabled => Theme.Success,
                FontSettingState.Missing => Theme.Muted,
                _ => Theme.Danger
            };
            _targetList.Items.Add(item);
        }
        _targetList.EndUpdate();

        if (result.LunarGameRunning)
        {
            SetStatus("请先退出 Lunar 游戏", "游戏运行时退出会覆盖配置，启动器可以保留", Theme.Danger);
            _fixButton.Enabled = false;
        }
        else if (result.ExistingCount == 0)
        {
            SetStatus("未检测到游戏目录", "请选择 Lunar 客户端的我的世界路径", Theme.Warning);
            _directoryHint.ForeColor = Theme.Warning;
            _fixButton.Enabled = false;
        }
        else if (result.IsHealthy)
        {
            SetStatus("当前状态正常", $"已检测 {result.ExistingCount} 个配置，Custom Fonts 均已关闭", Theme.Success);
            _directoryHint.ForeColor = Theme.Muted;
            _fixButton.Enabled = true;
        }
        else
        {
            SetStatus("检测到字体错位风险", $"有 {result.EnabledCount} 个配置启用了 Custom Fonts", Theme.Warning);
            _directoryHint.ForeColor = Theme.Muted;
            _fixButton.Enabled = true;
        }
    }

    private void ExportReport()
    {
        if (_lastResult is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出诊断报告",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"RainAura-Lunar诊断-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, LunarConfigService.BuildReport(_lastResult), new UTF8Encoding(true));
        Log("诊断报告已成功导出");
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
            Log("已打开 GitHub 项目主页");
        }
        catch (Exception ex) { ShowError("无法打开 GitHub：" + ex.Message); }
    }

    private static Image? LoadGitHubImage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RainAura.GitHubIcon");
        if (stream is null) return null;
        using var icon = new Icon(stream, new Size(18, 18));
        return icon.ToBitmap();
    }

    private void SetBusy(bool busy, string? title = null, string? detail = null)
    {
        _progress.Visible = busy;
        _scanButton.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
        _reportButton.Enabled = !busy;
        if (busy)
        {
            _fixButton.Enabled = false;
            SetStatus(title ?? "处理中…", detail ?? string.Empty, Theme.Text);
        }
    }

    private void SetStatus(string title, string detail, Color color)
    {
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        _statusBar.BackColor = color;
    }

    private void ShowError(string message)
    {
        Log($"错误：{message}");
        SetStatus("操作未完成", message, Theme.Danger);
        MessageBox.Show(message, "RainAura Lunar Fixer", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Log(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private static Panel CreateSectionPanel() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Label SectionTitle(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
        ForeColor = Theme.Text
    };

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Theme.Text;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 243, 245);
        button.BackColor = Color.White;
        button.ForeColor = Theme.Text;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Microsoft YaHei UI", 9F);
    }
}
