using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cet6WordMachine;

internal sealed class AiHintException(string message) : Exception(message);

internal interface IHintGenerator
{
    Task<string> GenerateAsync(WordEntry entry, AiSettings settings, CancellationToken cancellationToken);
    Task TestConnectionAsync(AiSettings settings, CancellationToken cancellationToken);
}

internal sealed class AiServices(IAiSettingsStore settings, IHintGenerator generator)
{
    internal IAiSettingsStore Settings { get; } = settings;
    internal IHintGenerator Generator { get; } = generator;
    internal static AiServices CreateDefault() => new(new AiSettingsStore(), new DeepSeekHintClient());
}

internal sealed class DeepSeekHintClient : IHintGenerator
{
    internal const string ApiOrigin = "https://api.deepseek.com/";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    internal DeepSeekHintClient(HttpClient? client = null, TimeSpan? timeout = null)
    { _client = client ?? SharedClient; _timeout = timeout ?? TimeSpan.FromSeconds(45); }

    public async Task<string> GenerateAsync(WordEntry entry, AiSettings settings, CancellationToken cancellationToken)
    {
        settings.Validate(requireKey: true);
        if (string.IsNullOrWhiteSpace(entry.Word) || string.IsNullOrWhiteSpace(entry.Meaning))
            throw new AiHintException("这个词缺少单词或释义，暂时无法生成联想。");
        var payload = new
        {
            model = settings.Model,
            messages = new[]
            {
                new { role = "system", content = AiHintPrompt.SystemMessage },
                new { role = "user", content = JsonSerializer.Serialize(new { word = entry.Word, phonetic = entry.Phonetic, meaning = entry.Meaning }) },
            },
            thinking = new { type = "disabled" },
            temperature = 1.1,
            max_tokens = 384,
            stream = false,
        };
        using var request = CreateRequest(HttpMethod.Post, "chat/completions", settings);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var choice = result.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop")
                throw new AiHintException("这次生成没有完整结束，请重新生成。原联想未修改。");
            var content = choice.GetProperty("message").GetProperty("content").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(content)) throw new AiHintException("这次没有返回联想内容，请重试。");
            if (new StringInfo(content).LengthInTextElements > AiHintPrompt.MaximumCharacters)
                throw new AiHintException("这次结果超过 80 字，请重新生成。原联想未修改。");
            return content;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new AiHintException("DeepSeek 返回的内容格式无法读取，请稍后重试。"); }
    }

    public async Task TestConnectionAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        settings.Validate(requireKey: true);
        using var request = CreateRequest(HttpMethod.Get, "models", settings);
        using var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!result.RootElement.GetProperty("data").EnumerateArray().Any(model =>
                    model.TryGetProperty("id", out var id) && id.GetString() == settings.Model))
                throw new AiHintException("Key 已连接，但所选模型暂不可用，请切换模型后重试。");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        { throw new AiHintException("无法读取模型列表，请稍后重试。"); }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, AiSettings settings)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiOrigin), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new AiHintException((int)response.StatusCode switch
            {
                401 or 403 => "API Key 无效或没有访问权限，请到 AI 设置中检查。",
                402 => "DeepSeek 账号余额不足，请充值后重试。",
                429 => "请求过于频繁，请稍后再试。",
                400 or 404 or 422 => "接口或模型参数不受支持，请检查模型设置或更新程序。",
                >= 300 and < 400 => "接口返回了重定向，已停止请求以保护 API Key。",
                >= 500 => "DeepSeek 暂时繁忙，请稍后再试。",
                _ => "DeepSeek 请求未成功，请稍后重试。",
            });
            // Bound both download and parse size. Never surface remote error bodies,
            // headers or request data in UI/logs (they might echo a credential).
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + count > 65536) throw new AiHintException("接口返回内容过大，请稍后重试。");
                memory.Write(buffer, 0, count);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return JsonDocument.Parse(memory.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AiHintException("请求超时，请检查网络后重试。"); }
        catch (HttpRequestException) { throw new AiHintException("无法连接 DeepSeek，请检查网络后重试。"); }
        catch (IOException) { throw new AiHintException("连接中断，请检查网络后重试。"); }
        catch (JsonException) { throw new AiHintException("接口返回了无法读取的内容，请稍后重试。"); }
    }
}
