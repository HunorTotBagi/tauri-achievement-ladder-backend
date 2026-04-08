using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EndlessGuildExporter;

internal static class SimpleXlsxWriter
{
    public static async Task WriteSingleWorksheetAsync(
        string outputPath,
        string sheetName,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await WriteXmlEntryAsync(archive, "[Content_Types].xml", WriteContentTypesAsync, cancellationToken);
            await WriteXmlEntryAsync(archive, "_rels/.rels", WriteRootRelationshipsAsync, cancellationToken);
            await WriteXmlEntryAsync(archive, "xl/workbook.xml", writer => WriteWorkbookAsync(writer, sheetName), cancellationToken);
            await WriteXmlEntryAsync(archive, "xl/_rels/workbook.xml.rels", WriteWorkbookRelationshipsAsync, cancellationToken);
            await WriteXmlEntryAsync(archive, "xl/worksheets/sheet1.xml", writer => WriteWorksheetAsync(writer, header, rows), cancellationToken);
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static async Task WriteXmlEntryAsync(
        ZipArchive archive,
        string entryName,
        Func<XmlWriter, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false
        };

        await using var writer = XmlWriter.Create(entryStream, settings);
        cancellationToken.ThrowIfCancellationRequested();
        await writeAsync(writer);
        await writer.FlushAsync();
    }

    private static Task WriteContentTypesAsync(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", "/xl/workbook.xml");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", "/xl/worksheets/sheet1.xml");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteRootRelationshipsAsync(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteWorkbookAsync(XmlWriter writer, string sheetName)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        writer.WriteStartElement("sheets");
        writer.WriteStartElement("sheet");
        writer.WriteAttributeString("name", sheetName);
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteWorkbookRelationshipsAsync(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
        writer.WriteAttributeString("Target", "worksheets/sheet1.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteWorksheetAsync(
        XmlWriter writer,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetData");

        WriteRow(writer, 1, header);

        for (var index = 0; index < rows.Count; index++)
        {
            WriteRow(writer, index + 2, rows[index]);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IReadOnlyList<string> values)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString());

        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            var cellReference = GetColumnName(columnIndex + 1) + rowIndex;
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", cellReference);
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");

            var sanitizedValue = SanitizeForXml(values[columnIndex]);
            if (!string.Equals(sanitizedValue, sanitizedValue.Trim(), StringComparison.Ordinal))
            {
                writer.WriteAttributeString("xml", "space", null, "preserve");
            }

            writer.WriteString(sanitizedValue);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string GetColumnName(int columnNumber)
    {
        var builder = new StringBuilder();

        while (columnNumber > 0)
        {
            columnNumber--;
            builder.Insert(0, (char)('A' + (columnNumber % 26)));
            columnNumber /= 26;
        }

        return builder.ToString();
    }

    private static string SanitizeForXml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
