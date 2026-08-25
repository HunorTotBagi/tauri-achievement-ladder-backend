using System.Globalization;
using Tauri.Core.Models;

namespace Tauri.Core.Infrastructure;

public static class RareItemCatalog
{
    public static IReadOnlyList<RareItemDefinition> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find rare item list: {path}", path);
        }

        var items = new List<RareItemDefinition>();
        var seenIds = new HashSet<int>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.LastIndexOf('-');
            if (
                separatorIndex <= 0
                || !int.TryParse(
                    line[(separatorIndex + 1)..].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id
                )
                || id <= 0
            )
            {
                throw new FormatException($"Invalid rare item line: '{rawLine}'. Expected 'Name - ID'.");
            }

            var name = line[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException($"Rare item {id} has no name.");
            }

            if (seenIds.Add(id))
            {
                items.Add(new RareItemDefinition(id, name));
            }
        }

        return items;
    }
}
