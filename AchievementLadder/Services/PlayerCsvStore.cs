using System.Globalization;
using System.Text;
using System.Text.Json;
using AchievementLadder.Models;

namespace AchievementLadder.Services;

public sealed class PlayerCsvStore
{
    private static readonly JsonSerializerOptions FrontendJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _outputDirectory;

    public PlayerCsvStore(string outputDirectory)
    {
        _outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public async Task<string> WriteAsync(IEnumerable<Player> players, string relativePath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDirectory);

        var fullPath = Path.Combine(_outputDirectory, relativePath);
        var tmpPath = fullPath + ".tmp";
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, utf8))
        {
            await writer.WriteLineAsync(
                "\"Name\",\"Race\",\"Gender\",\"Class\",\"Realm\",\"Guild\",\"AchievementPoints\",\"HonorableKills\",\"Faction\""
            );

            foreach (var p in players)
            {
                ct.ThrowIfCancellationRequested();

                static string Q(string? s) => $"\"{(s ?? string.Empty).Replace("\"", "\"\"")}\"";

                var line = string.Join(",",
                    Q(p.Name),
                    p.Race.ToString(CultureInfo.InvariantCulture),
                    p.Gender.ToString(CultureInfo.InvariantCulture),
                    p.Class.ToString(CultureInfo.InvariantCulture),
                    Q(p.Realm),
                    Q(p.Guild),
                    p.AchievementPoints.ToString(CultureInfo.InvariantCulture),
                    p.HonorableKills.ToString(CultureInfo.InvariantCulture),
                    Q(p.Faction)
                );

                await writer.WriteLineAsync(line);
            }
        }

        File.Move(tmpPath, fullPath, overwrite: true);
        return fullPath;
    }

    public async Task<string> WriteTextAsync(string relativePath, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDirectory);

        var fullPath = Path.Combine(_outputDirectory, relativePath);
        var tmpPath = fullPath + ".tmp";
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, utf8))
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteAsync(content);
        }

        File.Move(tmpPath, fullPath, overwrite: true);
        return fullPath;
    }

    public async Task<string> WriteJsonAsync<T>(string relativePath, T value, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDirectory);

        var fullPath = Path.Combine(_outputDirectory, relativePath);
        var tmpPath = fullPath + ".tmp";

        await using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            ct.ThrowIfCancellationRequested();
            await JsonSerializer.SerializeAsync(stream, value, FrontendJsonOptions, ct);
        }

        File.Move(tmpPath, fullPath, overwrite: true);
        return fullPath;
    }
}
