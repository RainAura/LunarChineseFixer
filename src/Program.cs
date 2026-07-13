namespace RainAura.LunarFontFixer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
#if NETFRAMEWORK
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#else
            ApplicationConfiguration.Initialize();
#endif
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
                MessageBox.Show(e.Exception.Message, "RainAura Lunar Fixer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "程序启动失败：" + ex.Message,
                "RainAura Lunar Fixer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
