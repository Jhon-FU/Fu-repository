using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cet6WordMachine;

internal sealed class AiSettings
{
    public int Version { get; set; } = 1;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = DefaultModel;
    internal const string DefaultModel = "deepseek-flash";
    internal static readonly string[] SupportedModels = [DefaultModel, "deepseek-v4-pro"];

    internal void Validate(bool requireKey)
    {
        if (Version != 1 || !SupportedModels.Contains(Model)) throw new AiHintException("请选择受支持的 DeepSeek 模型。");
        if (requireKey && string.IsNullOrWhiteSpace(ApiKey)) throw new AiHintException("请先在 AI 设置中填写 DeepSeek API Key。");
        if (ApiKey is null || ApiKey.Length > 4096 || ApiKey.Any(c => c < 33 || c > 126)) throw new AiHintException("API Key 格式不正确，请重新粘贴，去掉空格和换行。");
    }
}

internal interface IAiSettingsStore
{
    AiSettings Load();
    void Save(AiSettings settings);
}

internal sealed class AiSettingsStore : IAiSettingsStore
{
    private readonly string _path;
    internal AiSettingsStore(string? directory = null)
    {
        // Intentionally separate from portable learning data and bank exports.
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WordMachineCommunity", "ai");
        _path = Path.Combine(directory, "deepseek-settings.dat");
    }

    public AiSettings Load()
    {
        if (!File.Exists(_path)) return new AiSettings();
        try
        {
            if (new FileInfo(_path).Length > 65536) throw new InvalidDataException();
            var plain = WindowsUserProtection.Unprotect(File.ReadAllBytes(_path));
            try
            {
                var settings = JsonSerializer.Deserialize<AiSettings>(plain) ?? throw new InvalidDataException();
                settings.Validate(requireKey: false);
                return settings;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or AiHintException)
        {
            throw new AiHintException("本机 AI 配置无法读取。请在 AI 设置中重新填写 Key；学习记录不受影响。");
        }
    }

    public void Save(AiSettings settings)
    {
        settings.Validate(requireKey: false);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (settings.ApiKey.Length == 0)
            {
                try { File.Delete(_path); } catch (DirectoryNotFoundException) { }
                return;
            }
            var plain = JsonSerializer.SerializeToUtf8Bytes(settings);
            byte[] encrypted;
            try { encrypted = WindowsUserProtection.Protect(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(encrypted); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new AiHintException("AI 配置保存失败，原配置未替换。请检查文件占用或权限后重试。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal static class WindowsUserProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { internal int Length; internal IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    internal static byte[] Protect(byte[] input) => Transform(input, protect: true);
    internal static byte[] Unprotect(byte[] input) => Transform(input, protect: false);
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            // UI_FORBIDDEN, without LOCAL_MACHINE: bind protection to this Windows user.
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows 无法保护或解密 AI 配置。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) { Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
}
