using System.Globalization;
using System.Net;
using System.Text;

namespace GuildNameBruteForcer;

internal sealed record DictionarySource(
    string Key,
    string DisplayName,
    string FileName,
    string SourceUrl);

internal static class DictionaryWordList
{
    private static readonly DictionarySource HungarianSource = new(
        "hu",
        "Hungarian",
        "hu_HU.dic",
        "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/hu_HU/hu_HU.dic");

    private static readonly DictionarySource EnglishSource = new(
        "en",
        "English (US)",
        "en_US.dic",
        "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.dic");

    public static DictionarySource GetSource(DictionaryLanguage language) =>
        language switch
        {
            DictionaryLanguage.Hungarian => HungarianSource,
            DictionaryLanguage.English => EnglishSource,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    public static string GetDefaultPath(string projectRoot, DictionarySource source) =>
        Path.Combine(projectRoot, "Input", source.FileName);

    public static async Task EnsureDefaultFileExistsAsync(
        string dictionaryPath,
        DictionarySource source,
        CancellationToken cancellationToken)
    {
        if (File.Exists(dictionaryPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(dictionaryPath)
            ?? throw new InvalidOperationException("The dictionary path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = dictionaryPath + ".download";
        Console.WriteLine($"{source.DisplayName} dictionary is missing; downloading the LibreOffice word list...");
        Console.WriteLine($"  Source: {source.SourceUrl}");

        try
        {
            using var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
            using var response = await client.GetAsync(
                source.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            ValidateDictionaryHeader(temporaryPath);
            File.Move(temporaryPath, dictionaryPath, overwrite: true);
            Console.WriteLine($"  Saved: {dictionaryPath}");
            Console.WriteLine();
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static IReadOnlyList<string> LoadCandidates(
        string dictionaryPath,
        int minLength,
        int maxLength)
    {
        if (!File.Exists(dictionaryPath))
        {
            throw new FileNotFoundException($"Dictionary file not found: {dictionaryPath}", dictionaryPath);
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(dictionaryPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var firstLine = true;
        while (reader.ReadLine() is { } line)
        {
            if (firstLine)
            {
                firstLine = false;
                if (int.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }
            }

            var candidate = ParseHunspellEntry(line);
            if (candidate is null ||
                candidate.Length < minLength ||
                candidate.Length > maxLength ||
                !IsGuildNameCandidate(candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        return candidates
            .OrderBy(candidate => candidate.Length)
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? ParseHunspellEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var tabIndex = line.IndexOf('\t');
        var wordAndFlags = tabIndex >= 0 ? line[..tabIndex] : line;
        var flagIndex = FindUnescapedSlash(wordAndFlags);
        var word = flagIndex >= 0 ? wordAndFlags[..flagIndex] : wordAndFlags;

        word = word
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);

        var normalizedWhitespace = string.Join(
            ' ',
            word.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalizedWhitespace.Length == 0 ? null : normalizedWhitespace;
    }

    private static int FindUnescapedSlash(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '/')
            {
                continue;
            }

            var precedingBackslashes = 0;
            for (var backIndex = index - 1; backIndex >= 0 && value[backIndex] == '\\'; backIndex--)
            {
                precedingBackslashes++;
            }

            if (precedingBackslashes % 2 == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsGuildNameCandidate(string candidate) =>
        candidate.All(character => char.IsLetter(character) || character == ' ');

    private static void ValidateDictionaryHeader(string dictionaryPath)
    {
        using var reader = new StreamReader(dictionaryPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine();
        if (!int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var entryCount) || entryCount <= 0)
        {
            throw new InvalidDataException("The downloaded dictionary has an invalid Hunspell header.");
        }
    }
}
