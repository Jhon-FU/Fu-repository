using System.Text.Json;

namespace Cet6WordMachine;

internal sealed class MistakeRestState
{
    public int Version { get; set; } = 1;
    public DateTimeOffset? HiddenUntilUtc { get; set; }
    public DateTimeOffset? RetryAfterUtc { get; set; }
}

internal interface IMistakeRestStore
{
    MistakeRestState Load();
    void Save(MistakeRestState state);
}

// This small sidecar contains only rest deadlines, never grades or learning data.
internal sealed class MistakeRestStore(string directory) : IMistakeRestStore
{
    private readonly string _path = Path.Combine(directory, "mistake-widget.json");
    public MistakeRestState Load()
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 8192) throw new InvalidDataException("休息状态文件过大。");
        var state = JsonSerializer.Deserialize<MistakeRestState>(File.ReadAllBytes(_path));
        if (state is null || state.Version != 1 || (state.HiddenUntilUtc is not null && state.RetryAfterUtc is not null))
            throw new InvalidDataException("休息状态格式无法读取。");
        return state;
    }
    public void Save(MistakeRestState state)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, state); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed class MistakeRestPolicy
{
    internal static readonly TimeSpan RestDuration = TimeSpan.FromHours(1);
    internal static readonly TimeSpan RetryDuration = TimeSpan.FromMinutes(2);
    private readonly IMistakeRestStore _store;
    private readonly TimeProvider _clock;
    private MistakeRestState _state = new();
    internal bool NeedsReload { get; private set; }
    internal string Error { get; private set; } = "";
    internal MistakeRestPolicy(IMistakeRestStore store, TimeProvider? clock = null)
    { _store = store; _clock = clock ?? TimeProvider.System; Reload(); }

    internal bool IsHidden => !NeedsReload && _state.HiddenUntilUtc > _clock.GetUtcNow();
    internal TimeSpan RetryRemaining
    {
        get { var now = _clock.GetUtcNow(); return _state.RetryAfterUtc is { } until && until > now ? until - now : TimeSpan.Zero; }
    }
    internal bool CanAttempt => !NeedsReload && !IsHidden && RetryRemaining == TimeSpan.Zero;

    internal bool TryBegin()
    {
        if (NeedsReload && !Reload()) return false;
        if (!CanAttempt) return false;
        // Reserve the attempt before opening the question, so a quick restart
        // during the quiz does not clear its initial two-minute cooldown.
        return Commit(new() { RetryAfterUtc = _clock.GetUtcNow() + RetryDuration });
    }
    internal bool Finish(bool passed) => Commit(passed
        ? new() { HiddenUntilUtc = _clock.GetUtcNow() + RestDuration }
        : new() { RetryAfterUtc = _clock.GetUtcNow() + RetryDuration });

    private bool Reload()
    {
        try { _state = _store.Load(); NeedsReload = false; Error = ""; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { NeedsReload = true; Error = "休息状态读取失败，请检查数据目录后重试。"; return false; }
    }
    private bool Commit(MistakeRestState candidate)
    {
        try { _store.Save(candidate); _state = candidate; Error = ""; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Even if persistence fails, a failed/aborted attempt still waits
            // two minutes in this running process. Never hide on a failed save.
            _state = new() { RetryAfterUtc = _clock.GetUtcNow() + RetryDuration };
            Error = "休息状态保存失败，卡片保持显示。"; return false;
        }
    }
}

internal static class WidgetScreenBounds
{
    internal static Rectangle Clamp(Rectangle bounds, Rectangle area)
    {
        var width = Math.Clamp(bounds.Width, 1, Math.Max(1, area.Width));
        var height = Math.Clamp(bounds.Height, 1, Math.Max(1, area.Height));
        return new Rectangle(Math.Clamp(bounds.X, area.Left, area.Right - width),
            Math.Clamp(bounds.Y, area.Top, area.Bottom - height), width, height);
    }
}
