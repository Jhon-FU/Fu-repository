using System.Diagnostics;

namespace Cet6WordMachine;

internal static class Program
{
    private const string MutexName = @"Local\WordMachineCommunity.SingleInstance.v1";

    [STAThread]
    private static void Main(string[] args)
    {
        var diagnosticInstance = args.Contains(
            "--diagnostic",
            StringComparer.OrdinalIgnoreCase);
        var portableInstance = args.Contains(
            "--portable",
            StringComparer.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));
#if TESTMODE
        var testMode = true;
#else
        var testMode = false;
#endif
        if (diagnosticInstance &&
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR")))
        {
            Environment.SetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_DATA_DIR",
                Path.Combine(Path.GetTempPath(), "WordMachineCommunityDiagnostics"));
        }

        if (portableInstance)
        {
            Environment.SetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_DATA_DIR",
                Path.Combine(AppContext.BaseDirectory, "data"));
        }

#if TESTMODE
        if (testMode)
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_DATA_DIR")))
            {
                Environment.SetEnvironmentVariable(
                    "WORD_MACHINE_COMMUNITY_DATA_DIR",
                    Path.Combine(AppContext.BaseDirectory, "test-data"));
            }

            Environment.SetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_TEST_MODE",
                "1");
            Environment.SetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_SKIP_AUTOSTART",
                "1");
        }
#endif

        if (diagnosticInstance)
        {
            Environment.SetEnvironmentVariable("WORD_MACHINE_COMMUNITY_SKIP_AUTOSTART", "1");
        }

        var dateArgument = args.FirstOrDefault(
            item => item.StartsWith("--date=", StringComparison.OrdinalIgnoreCase));
        if (dateArgument is not null)
        {
            Environment.SetEnvironmentVariable(
                "WORD_MACHINE_COMMUNITY_TODAY",
                dateArgument["--date=".Length..]);
        }

        var mutexName = diagnosticInstance
            ? @"Local\WordMachineCommunity.Diagnostics"
            : testMode
                ? @"Local\WordMachineCommunity.TestMode"
                : MutexName;
        using var mutex = new Mutex(true, mutexName, out var firstInstance);
        if (!firstInstance)
        {
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            MessageBox.Show(
                "程序遇到意外错误。请再次打开；如果问题持续，可在任务管理器结束进程。\n\n" +
                eventArgs.Exception.Message,
                "六级单词机",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                MessageBox.Show(
                    "程序遇到意外错误。\n\n" + exception.Message,
                    "六级单词机",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        // Startup registration is an explicit menu action, never a launch side effect.
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            MessageBox.Show(
                "无法启动六级单词机。\n\n" + exception.Message,
                "六级单词机",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
