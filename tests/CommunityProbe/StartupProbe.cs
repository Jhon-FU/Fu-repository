using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Cet6WordMachine;

internal sealed class MemoryStartupEntryStore : IStartupEntryStore
{
    public string? Command { get; set; }
    public int Writes { get; private set; }
    public bool RejectWrites { get; set; }
    public bool IgnoreWrites { get; set; }
    public string? ReadCommand() => Command;
    public void WriteCommand(string command)
    {
        if (RejectWrites) throw new UnauthorizedAccessException("probe access denied");
        Writes++;
        if (!IgnoreWrites) Command = command;
    }
}

internal static class StartupProbe
{
    public static void Run(Action<bool, string> check, string root)
    {
        var folder = Path.Combine(root, "startup 空格路径");
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "WordMachineCommunity.exe");
        File.WriteAllBytes(executable, []);
        var expected = $"\"{executable}\" --portable";
        var store = new MemoryStartupEntryStore();
        var registration = new StartupRegistration(store, executable, true);
        check(registration.GetStatus().CanEnable && store.Writes == 0, "Opening startup menu never registers implicitly");
        check(registration.Enable().Success && store.Command == expected, "Startup command quotes Chinese space path and preserves portable mode");
        check(!registration.GetStatus().CanEnable, "Registered menu reports current executable");
        check(registration.Enable().Success && store.Writes == 1, "Repeated registration is idempotent");
        var moved = Path.Combine(folder, "moved.exe"); File.WriteAllBytes(moved, []);
        var movedRegistration = new StartupRegistration(store, moved, false);
        check(movedRegistration.GetStatus().MenuText.Contains("更新"), "Moving app offers startup path repair");
        check(movedRegistration.Enable().Success && store.Command == $"\"{moved}\"", "Path repair replaces only one entry without portable flag");
        store.Command = null;
        check(registration.GetStatus().CanEnable && store.Writes == 2, "External removal is detected without automatic restoration");
        var blocked = new StartupRegistration(store, executable, true, blocked: true);
        check(!blocked.GetStatus().CanEnable && !blocked.Enable().Success && store.Writes == 2, "Diagnostic registration cannot write");
        check(!StartupRegistration.ForCurrentProcess().GetStatus().CanEnable, "Probe process startup guard remains active");
        store.RejectWrites = true;
        check(!registration.Enable().Success && store.Command is null, "Denied startup write reports failure");
        store.RejectWrites = false; store.IgnoreWrites = true;
        check(!registration.Enable().Success, "Silent write failure is caught by readback");
        store.IgnoreWrites = false;
        check(registration.Enable().Success, "Registration retries successfully after failure");
        check(!new StartupRegistration(store, Path.Combine(folder, "missing.exe"), false).Enable().Success, "Missing executable cannot be registered");
        check(!new StartupRegistration(store, Path.Combine(folder, "dotnet.exe"), false).Enable().Success, "Dotnet host cannot become an invalid startup command");
        check(!new StartupRegistration(store, "relative.exe", false).Enable().Success, "Relative startup path rejected");
        var longFolder = Path.Combine(folder, new string('a', 90), new string('b', 90), new string('c', 90));
        Directory.CreateDirectory(longFolder);
        var longExe = Path.Combine(longFolder, "app.exe"); File.WriteAllBytes(longExe, []);
        check(!new StartupRegistration(store, longExe, true).Enable().Success, "Windows Run command length limit is enforced");

        TestRegistryAndRemoval(check, root, executable);
    }

    private static void TestRegistryAndRemoval(Action<bool, string> check, string root, string executable)
    {
        var testRoot = @"Software\WordMachineCommunity.Tests\" + Guid.NewGuid().ToString("N");
        var runKey = testRoot + @"\Run";
        var realStore = new RegistryStartupEntryStore();
        var originalEntry = realStore.ReadCommand();
        try
        {
            var store = new RegistryStartupEntryStore(runKey);
            var registration = new StartupRegistration(store, executable, true);
            var result = registration.Enable();
            check(result.Success && store.ReadCommand() == $"\"{executable}\" --portable", "Registry adapter writes and verifies isolated test key: " + result.Message);
            using (var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true))
                key!.SetValue("CET6WordMachine", "unrelated personal entry sentinel");
            var originalScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, StartupRegistration.RemovalFileName));
            check(originalScript.Contains("'" + StartupRegistration.RunKeyPath + "'") &&
                originalScript.Contains("'" + StartupRegistration.EntryName + "'"), "Shipped CMD matches production entry and key");
            // Exercise the shipped script against a fresh, non-startup key only.
            var testScript = Path.Combine(root, StartupRegistration.RemovalFileName);
            File.WriteAllText(testScript, originalScript.Replace(StartupRegistration.RunKeyPath, runKey), new UTF8Encoding(false));
            int RunScript()
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    Arguments = $"/d /c \"\"{testScript}\" --quiet\"",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000)) { process.Kill(entireProcessTree: true); throw new IOException("Removal CMD timed out."); }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new IOException(output.Result + error.Result);
                return process.ExitCode;
            }
            check(RunScript() == 0 && store.ReadCommand() is null, "One-click CMD removes isolated startup entry");
            using (var key = Registry.CurrentUser.OpenSubKey(runKey))
                check((string?)key!.GetValue("CET6WordMachine") == "unrelated personal entry sentinel", "Removal leaves personal and unrelated entries untouched");
            check(RunScript() == 0, "Repeated CMD removal succeeds when value is already absent");
            Registry.CurrentUser.DeleteSubKeyTree(runKey);
            check(RunScript() == 0, "Removal succeeds when startup key is absent");
            using (var absent = Registry.CurrentUser.OpenSubKey(runKey))
                check(absent is null, "Removal does not recreate missing startup key");
            check(realStore.ReadCommand() == originalEntry, "Tests leave real community startup entry unchanged");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }
}
