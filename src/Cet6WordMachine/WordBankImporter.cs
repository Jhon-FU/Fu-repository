using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cet6WordMachine;

internal sealed class WordBankImportResult
{
    public List<WordEntry> Words { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public int DuplicateCount { get; set; }
    public bool CanImport => Words.Count is > 0 and <= WordBankImporter.MaximumWords && Errors.Count == 0;
}

internal static class WordBankImporter
{
    public const int MaximumWords = 50_000;
    public const long MaximumFileBytes = 20 * 1024 * 1024;
    private static readonly Regex WordPattern = new(@"^[A-Za-z][A-Za-z .\-'’]*$", RegexOptions.CultureInvariant);
    private sealed record Row(int Number, List<string> Cells);

    public static WordBankImportResult ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("词库文件不能超过 20 MB。");
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" => ConvertRows(ReadWorkbook(path)),
            ".json" => ReadJson(ReadText(path)),
            ".csv" or ".tsv" or ".txt" => ReadDelimited(ReadText(path)),
            _ => throw new InvalidDataException("请选择 CSV、TSV、TXT、JSON 或 XLSX 文件；旧版 XLS 请另存为 XLSX。"),
        };
    }

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
    }

    public static WordBankImportResult ReadDelimited(string text)
    {
        if (text.Length > MaximumFileBytes) throw new InvalidDataException("粘贴内容过大。");
        text = text.TrimStart('\uFEFF');
        var first = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var counts = new Dictionary<char, int> { ['\t'] = 0, [','] = 0, [';'] = 0 };
        var quoted = false;
        foreach (var c in first)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && counts.ContainsKey(c)) counts[c]++;
        }
        var delimiter = counts.OrderByDescending(c => c.Value).First().Key;
        return ConvertRows(ParseDelimited(text, delimiter));
    }

    private static List<Row> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<Row>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var line = 1;
        var rowLine = 1;
        void AddField() { fields.Add(field.ToString()); field.Clear(); afterQuote = false; }
        void AddRow()
        {
            AddField();
            if (fields.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(new(rowLine, fields));
            fields = [];
            if (rows.Count > MaximumWords + 1) throw new InvalidDataException("最多导入 50,000 个词条。");
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; afterQuote = true; }
                }
                else { field.Append(c); if (c == '\n') line++; }
                continue;
            }
            if (c == delimiter) AddField();
            else if (c is '\r' or '\n')
            {
                AddRow();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                rowLine = ++line;
            }
            else if (c == '"')
            {
                if (field.Length != 0 || afterQuote) throw new InvalidDataException($"第 {line} 行引号位置错误；包含引号的字段请用双引号包围。 ");
                quoted = true;
            }
            else
            {
                if (afterQuote && !char.IsWhiteSpace(c)) throw new InvalidDataException($"第 {line} 行引号后存在多余字符。");
                if (!afterQuote) field.Append(c);
            }
        }
        if (quoted) throw new InvalidDataException($"第 {rowLine} 行开始的引号未闭合。");
        if (field.Length > 0 || fields.Count > 0 || afterQuote) AddRow();
        return rows;
    }

    private static string Header(string value) => value.Trim().ToLowerInvariant() switch
    {
        "word" or "name" or "单词" or "词条" or "英文" => "word",
        "meaning" or "translation" or "释义" or "中文" or "中文释义" or "意思" => "meaning",
        "phonetic" or "音标" => "phonetic",
        "hint" or "联想" or "记忆提示" => "hint",
        "example" or "例句" => "example",
        _ => "",
    };

    private static WordBankImportResult ConvertRows(List<Row> rows)
    {
        var result = new WordBankImportResult();
        if (rows.Count == 0) { result.Errors.Add("没有可导入的词条。"); return result; }
        var headers = rows[0].Cells.Select(Header).ToList();
        var hasHeader = headers.Count(h => h != "") >= 2 && (headers.Contains("word") || headers.Contains("meaning"));
        var columns = new Dictionary<string, int>();
        if (hasHeader)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i] == "") continue;
                if (!columns.TryAdd(headers[i], i)) result.Errors.Add($"表头重复：{rows[0].Cells[i]}");
            }
            if (!columns.ContainsKey("word") || !columns.ContainsKey("meaning"))
                result.Errors.Add("表头必须包含“单词”和“释义”（或 word、meaning）。");
            if (result.Errors.Count > 0) return result;
        }
        else
        {
            columns = new() { ["word"] = 0, ["meaning"] = 1, ["phonetic"] = 2, ["hint"] = 3, ["example"] = 4 };
            result.Warnings.Add("未检测到表头，按“单词、释义、音标、联想、例句”的列顺序读取。");
        }
        var seen = new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(hasHeader ? 1 : 0))
        {
            string Get(string key) => columns.TryGetValue(key, out var i) && i < row.Cells.Count ? row.Cells[i].Trim() : "";
            Add(result, seen, new WordEntry
            {
                Word = Get("word"), Meaning = Get("meaning"), Phonetic = Get("phonetic"),
                Hint = Get("hint"), Example = Get("example"),
            }, row.Number);
        }
        if (result.Words.Count == 0) result.Errors.Add("没有有效词条。至少需要一行单词和释义。");
        if (result.Words.Count > MaximumWords) result.Errors.Add("最多导入 50,000 个词条，请拆分后重试。");
        return result;
    }

    public static WordBankImportResult ReadJson(string text)
    {
        if (text.Length > MaximumFileBytes) throw new InvalidDataException("JSON 内容过大。");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("formatVersion", out var version) && version.GetInt32() != 1)
                throw new InvalidDataException("不支持此 JSON 词库版本。");
            if (!root.TryGetProperty("words", out root)) throw new InvalidDataException("词库对象必须包含 words 数组。");
        }
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON 必须是词条数组或包含 words 数组的词库对象。");
        if (root.GetArrayLength() > MaximumWords) throw new InvalidDataException("最多导入 50,000 个词条。");
        var result = new WordBankImportResult();
        var seen = new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var row in root.EnumerateArray())
        {
            index++;
            try
            {
                var entry = row.Deserialize<WordEntry>(WordBankCatalog.JsonOptions)
                    ?? throw new InvalidDataException("词条不能为 null。");
                Add(result, seen, entry, index);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            { result.Errors.Add($"第 {index} 项：{ex.Message}"); }
        }
        if (result.Words.Count == 0) result.Errors.Add("没有有效词条。");
        return result;
    }

    private static void Add(WordBankImportResult result, Dictionary<string, WordEntry> seen, WordEntry entry, int row)
    {
        try
        {
            entry.Word = (entry.Word ?? "").Trim();
            entry.Meaning = (entry.Meaning ?? "").Trim();
            entry.Phonetic = (entry.Phonetic ?? "").Trim();
            entry.Hint = (entry.Hint ?? "").Trim();
            entry.Example = (entry.Example ?? "").Trim();
            entry.Level ??= "";
            entry.StudyTier ??= "";
            entry.FrequencyNote ??= "";
            ValidateEntry(entry);
            if (seen.TryGetValue(entry.Word, out var old))
            {
                result.DuplicateCount++;
                if (old.Meaning != entry.Meaning || old.Phonetic != entry.Phonetic || old.Hint != entry.Hint || old.Example != entry.Example)
                    result.Errors.Add($"第 {row} 行：{entry.Word} 重复且内容不同，请合并释义或删除重复行。");
                return;
            }
            entry.FrequencyRank = result.Words.Count + 1;
            seen.Add(entry.Word, entry);
            result.Words.Add(entry);
        }
        catch (InvalidDataException ex) { result.Errors.Add($"第 {row} 行：{ex.Message}"); }
    }

    public static void ValidateEntry(WordEntry? entry)
    {
        if (entry is null) throw new InvalidDataException("词条不能为 null。");
        if (string.IsNullOrWhiteSpace(entry.Word) || entry.Word.Length > 100 || !WordPattern.IsMatch(entry.Word) || entry.Word != entry.Word.Trim())
            throw new InvalidDataException("单词须以英文字母开头，最多 100 字符，可含空格、连字符、句点和撇号。");
        if (string.IsNullOrWhiteSpace(entry.Meaning)) throw new InvalidDataException($"{entry.Word} 缺少释义。");
        foreach (var value in new[] { entry.Meaning, entry.Phonetic, entry.Hint, entry.Example })
        {
            if (value is null || value.Length > 5000 || value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
                throw new InvalidDataException($"{entry.Word} 的字段为空值、超过 5,000 字符或含非法控制字符。");
        }
    }

    private static List<Row> ReadWorkbook(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 5000 || archive.Entries.Sum(e => e.Length) > 80 * 1024 * 1024)
            throw new InvalidDataException("Excel 解压后过大，请仅保留词库数据后重试。");
        XDocument Xml(string part)
        {
            var entry = archive.GetEntry(part) ?? throw new InvalidDataException($"Excel 缺少必要内容：{part}");
            if (entry.Length > 40 * 1024 * 1024) throw new InvalidDataException("Excel 工作表过大。");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 40 * 1024 * 1024 });
            return XDocument.Load(reader);
        }
        var workbook = Xml("xl/workbook.xml");
        var sheet = workbook.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet")
            ?? throw new InvalidDataException("Excel 没有工作表。");
        var rid = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
        var rel = Xml("xl/_rels/workbook.xml.rels").Descendants().FirstOrDefault(e => e.Name.LocalName == "Relationship" && (string?)e.Attribute("Id") == rid)
            ?? throw new InvalidDataException("无法定位首张工作表。");
        if ((string?)rel.Attribute("TargetMode") == "External") throw new InvalidDataException("不支持外部链接工作表。");
        var target = (string?)rel.Attribute("Target") ?? "";
        var part = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        if (part.Contains("..") || part.Contains('\\') || part.Contains(':')) throw new InvalidDataException("Excel 工作表路径无效。");
        var shared = archive.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml("xl/sharedStrings.xml").Descendants()
            .Where(e => e.Name.LocalName == "si").Select(e => string.Concat(e.Descendants().Where(n => n.Name.LocalName == "t").Select(n => n.Value))).ToList();
        var rows = new List<Row>();
        foreach (var row in Xml(part).Descendants().Where(e => e.Name.LocalName == "row"))
        {
            var cells = new List<string>();
            var number = int.TryParse((string?)row.Attribute("r"), out var n) ? n : rows.Count + 1;
            foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
            {
                if (cell.Elements().Any(e => e.Name.LocalName == "f"))
                    throw new InvalidDataException($"第 {number} 行含公式，请先粘贴为值再导入。");
                var reference = (string?)cell.Attribute("r") ?? "";
                var column = 0;
                foreach (var c in reference.TakeWhile(char.IsLetter)) column = checked(column * 26 + char.ToUpperInvariant(c) - 'A' + 1);
                if (column == 0) column = cells.Count + 1;
                if (column > 100) throw new InvalidDataException("工作表列数超过 100，请整理为单词表再导入。");
                while (cells.Count < column) cells.Add("");
                var type = (string?)cell.Attribute("t");
                var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                if (type == "s")
                {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Count) throw new InvalidDataException("Excel 共享字符串索引无效。");
                    value = shared[index];
                }
                else if (type == "inlineStr") value = string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                else if (type == "e") throw new InvalidDataException($"第 {number} 行含 Excel 错误值。");
                cells[column - 1] = value;
            }
            if (cells.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(new(number, cells));
            if (rows.Count > MaximumWords + 1) throw new InvalidDataException("最多导入 50,000 个词条。");
        }
        return rows;
    }

    public static void Export(WordBankPackage bank, string path)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(bank, WordBankCatalog.JsonOptions), new UTF8Encoding(false));
            return;
        }
        string Csv(string text)
        {
            // Spreadsheet applications may evaluate formula-like CSV cells.
            if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("word,meaning,phonetic,hint,example");
        foreach (var w in bank.Words) writer.WriteLine(string.Join(',', new[] { w.Word, w.Meaning, w.Phonetic, w.Hint, w.Example }.Select(Csv)));
    }
}
