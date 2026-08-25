using Tauri.Core.Infrastructure;

namespace Tauri.Core.Tests;

public sealed class RareItemCatalogTests
{
    [Fact]
    public void Load_ParsesNamesAndIdsUsingLastDash()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "rare-items.txt");
            File.WriteAllText(
                path,
                "The Plague Bearer - 22818\nA Name-With-Dashes - 23075\n"
            );

            var items = RareItemCatalog.Load(path);

            Assert.Equal(2, items.Count);
            Assert.Equal("The Plague Bearer", items[0].Name);
            Assert.Equal(22818, items[0].Id);
            Assert.Equal("A Name-With-Dashes", items[1].Name);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
