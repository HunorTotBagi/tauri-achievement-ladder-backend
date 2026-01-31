using System.Globalization;
using System.Text;
using AchievementLadder.Models;

namespace AchievementLadder.Services;

public sealed class PlayerCsvStore
{
    private readonly IWebHostEnvironment _env;

    public PlayerCsvStore(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task WriteAsync(IEnumerable<Player> players, string relativePath, CancellationToken ct = default)
    {
        var projectRoot = _env.ContentRootPath;
        var outputDir = Path.GetFullPath(Path.Combine(
            projectRoot,
            "..", "..", "..",
            "tauriachievements.github.io",
            "src"
        ));

        Directory.CreateDirectory(outputDir);

        var fullPath = Path.Combine(outputDir, relativePath);

        var tmpPath = fullPath + ".tmp";

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, utf8))
        {
            await writer.WriteLineAsync("\"Name\",\"Race\",\"Gender\",\"Class\",\"Realm\",\"Guild\",\"AchievementPoints\",\"HonorableKills\",\"LastUpdated\",\"Faction\"");

            foreach (var p in players)
            {
                ct.ThrowIfCancellationRequested();

                var lastUpdated = ToDateTimeOffsetUtc(p.LastUpdated);
                var lastUpdatedStr = lastUpdated.ToString("yyyy-MM-dd HH:mm:ss.ffffffK", CultureInfo.InvariantCulture)
                    .Replace("Z", "+00");

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
                    Q(lastUpdatedStr),
                    Q(p.Faction)
                );

                await writer.WriteLineAsync(line);
            }
        }

        File.Move(tmpPath, fullPath, overwrite: true);
    }

    private static DateTimeOffset ToDateTimeOffsetUtc(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (dt.Kind == DateTimeKind.Local) return new DateTimeOffset(dt).ToUniversalTime();
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
    }

    public async Task WriteLastUpdatedAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var projectRoot = _env.ContentRootPath;
        var outputDir = Path.GetFullPath(Path.Combine(
            projectRoot,
            "..", "..", "..",
            "tauriachievements.github.io",
            "src"
        ));

        Directory.CreateDirectory(outputDir);

        var filePath = Path.Combine(outputDir, "lastUpdated.txt");
        var tmpPath = filePath + ".tmp";
        var content = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        await File.WriteAllTextAsync(tmpPath, content, Encoding.UTF8, ct);

        File.Move(tmpPath, filePath, overwrite: true);
    }
}
