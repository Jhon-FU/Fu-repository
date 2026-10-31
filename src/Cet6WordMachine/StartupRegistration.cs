using Microsoft.Win32;

namespace Cet6WordMachine;

internal interface IStartupEntryStore
{
    string? ReadCommand();
    void WriteCommand(string command);
}

internal sealed class RegistryStartupEntryStore(string keyPath = StartupRegistration.RunKeyPath) : IStartupEntryStore
{
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(StartupRegistration.EntryName, null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void WriteCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new IOException("无法打开当前用户的启动项注册表。");
        key.SetValue(StartupRegistration.EntryName, command, RegistryValueKind.String);
    }
}

internal sealed record StartupStatus(string MenuText, bool CanEnable, string Detail);
internal sealed record StartupResult(bool Success, string Message);

internal sealed class StartupRegistration(IStartupEntryStore store, string? executable, bool portable, bool blocked = false)
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string EntryName = "WordMachineCommunity";
    internal const string RemovalFileName = "撤销开机自启动.cmd";

    public static StartupRegistration ForCurrentProcess()
    {
        var args = Environment.GetCommandLineArgs();
        var portable = args.Contains("--portable", StringComparer.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));
        var blocked = args.Contains("--diagnostic", StringComparer.OrdinalIgnoreCase) ||
            Environment.GetEnvironmentVariable("WORD_MACHINE_COMMUNITY_SKIP_AUTOSTART") == "1";
#if TESTMODE
        blocked = true;
#endif
        return new StartupRegistration(new RegistryStartupEntryStore(), Environment.ProcessPath, portable, blocked);
    }

    internal static string BuildCommand(string? executable, bool portable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) ||
            executable.IndexOfAny(['\"', '\r', '\n']) >= 0 ||
            !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请从发布文件夹直接运行 WordMachineCommunity.exe 后再设置自启动。");
        if (!File.Exists(executable))
            throw new FileNotFoundException("当前程序路径已不存在，请重新打开程序后再设置。", executable);
        var command = $"\"{executable}\"" + (portable ? " --portable" : "");
        // Windows Run entries accept a command line of at most 260 characters.
        if (command.Length > 260)
            throw new InvalidDataException("程序路径过长，请移到较短的固定目录后再设置自启动。");
        return command;
    }

    public StartupStatus GetStatus()
    {
        if (blocked) return new("测试 / 诊断模式不设置自启动", false, "请使用正式版设置开机自启动。");
        try
        {
            var command = BuildCommand(executable, portable);
            var registered = store.ReadCommand();
            if (string.Equals(registered, command, StringComparison.OrdinalIgnoreCase))
                return new("开机自启动已添加 ✓", false, "下次登录 Windows 时启动当前程序。");
            return new(registered is null ? "一键开启开机自启动" : "更新开机自启动到当前程序", true,
                "为当前 Windows 用户添加启动项。");
        }
        catch (Exception ex) when (IsExpectedError(ex))
        {
            return new("一键开启开机自启动", true, "暂时无法读取启动状态：" + ex.Message);
        }
    }

    public StartupResult Enable()
    {
        if (blocked) return new(false, "测试 / 诊断模式不会写入开机启动项，请使用正式版。");
        try
        {
            var command = BuildCommand(executable, portable);
            if (!string.Equals(store.ReadCommand(), command, StringComparison.OrdinalIgnoreCase))
                store.WriteCommand(command);
            if (!string.Equals(store.ReadCommand(), command, StringComparison.OrdinalIgnoreCase))
                throw new IOException("写入后校验失败，请检查系统是否阻止修改启动项。");
            return new(true, $"已添加开机自启动，下次登录 Windows 生效。撤销请运行程序文件夹中的“{RemovalFileName}”。");
        }
        catch (Exception ex) when (IsExpectedError(ex))
        {
            return new(false, "未能添加开机自启动：" + ex.Message);
        }
    }

    private static bool IsExpectedError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException;
}
