using System.IO.Compression;
using System.Text;
using System.Xml;

namespace EndlessGuildExporter;

internal static class SimpleXlsxWriter
{
    private const string DefaultFontName = "Arial";

    internal enum CellValueKind
    {
        Text,
        Number,
        FormulaNumber
    }

    internal readonly record struct CellData(
        string Value,
        string? StyleKey = null,
        CellValueKind ValueKind = CellValueKind.Text,
        string? Formula = null);

    internal readonly record struct CellStyle(string FillColorHex, string FontColorHex, bool IsBold = false);

    internal readonly record struct DataValidation(string Sqref, string Formula1, string Type = "list", bool AllowBlank = false);

    public static async Task WriteSingleWorksheetAsync(
        string outputPath,
        string sheetName,
        IReadOnlyList<CellData> header,
        IReadOnlyList<IReadOnlyList<CellData>> rows,
        IReadOnlyDictionary<string, CellStyle>? cellStyles,
        IReadOnlyList<DataValidation>? dataValidations,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var styleDefinitions = cellStyles?.ToList() ?? [];
        var styleIndexByKey = BuildStyleIndexByKey(styleDefinitions);
        var hasStyles = styleDefinitions.Count > 0;
        var validations = dataValidations ?? [];

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

            await WriteXmlEntryAsync(archive, "[Content_Types].xml", writer => WriteContentTypesAsync(writer, hasStyles), cancellationToken);
            await WriteXmlEntryAsync(archive, "_rels/.rels", WriteRootRelationshipsAsync, cancellationToken);
            await WriteXmlEntryAsync(archive, "xl/workbook.xml", writer => WriteWorkbookAsync(writer, sheetName), cancellationToken);
            await WriteXmlEntryAsync(archive, "xl/_rels/workbook.xml.rels", writer => WriteWorkbookRelationshipsAsync(writer, hasStyles), cancellationToken);

            if (hasStyles)
            {
                await WriteXmlEntryAsync(archive, "xl/styles.xml", writer => WriteStylesAsync(writer, styleDefinitions), cancellationToken);
            }

            await WriteXmlEntryAsync(
                archive,
                "xl/worksheets/sheet1.xml",
                writer => WriteWorksheetAsync(writer, header, rows, styleIndexByKey, validations),
                cancellationToken);
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

    private static Task WriteContentTypesAsync(XmlWriter writer, bool hasStyles)
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

        if (hasStyles)
        {
            writer.WriteStartElement("Override");
            writer.WriteAttributeString("PartName", "/xl/styles.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            writer.WriteEndElement();
        }

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
        writer.WriteStartElement("calcPr");
        writer.WriteAttributeString("calcId", "0");
        writer.WriteAttributeString("calcMode", "auto");
        writer.WriteAttributeString("fullCalcOnLoad", "1");
        writer.WriteAttributeString("forceFullCalc", "1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteWorkbookRelationshipsAsync(XmlWriter writer, bool hasStyles)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
        writer.WriteAttributeString("Target", "worksheets/sheet1.xml");
        writer.WriteEndElement();

        if (hasStyles)
        {
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", "rId2");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
            writer.WriteAttributeString("Target", "styles.xml");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteWorksheetAsync(
        XmlWriter writer,
        IReadOnlyList<CellData> header,
        IReadOnlyList<IReadOnlyList<CellData>> rows,
        IReadOnlyDictionary<string, int> styleIndexByKey,
        IReadOnlyList<DataValidation> dataValidations)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("sheetData");

        WriteRow(writer, 1, header, styleIndexByKey);

        for (var index = 0; index < rows.Count; index++)
        {
            WriteRow(writer, index + 2, rows[index], styleIndexByKey);
        }

        writer.WriteEndElement();

        if (dataValidations.Count > 0)
        {
            WriteDataValidations(writer, dataValidations);
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static Task WriteStylesAsync(XmlWriter writer, IReadOnlyList<KeyValuePair<string, CellStyle>> styleDefinitions)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("styleSheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        WriteFonts(writer, styleDefinitions);
        WriteFills(writer, styleDefinitions);
        WriteBorders(writer);
        WriteCellStyleXfs(writer);
        WriteCellXfs(writer, styleDefinitions);
        WriteCellStyles(writer);

        writer.WriteStartElement("dxfs");
        writer.WriteAttributeString("count", "0");
        writer.WriteEndElement();

        writer.WriteStartElement("tableStyles");
        writer.WriteAttributeString("count", "0");
        writer.WriteAttributeString("defaultTableStyle", "TableStyleMedium2");
        writer.WriteAttributeString("defaultPivotStyle", "PivotStyleLight16");
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
        return Task.CompletedTask;
    }

    private static void WriteFonts(XmlWriter writer, IReadOnlyList<KeyValuePair<string, CellStyle>> styleDefinitions)
    {
        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", (styleDefinitions.Count + 1).ToString());

        writer.WriteStartElement("font");
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("color");
        writer.WriteAttributeString("theme", "1");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", DefaultFontName);
        writer.WriteEndElement();
        writer.WriteStartElement("family");
        writer.WriteAttributeString("val", "2");
        writer.WriteEndElement();
        writer.WriteStartElement("scheme");
        writer.WriteAttributeString("val", "minor");
        writer.WriteEndElement();
        writer.WriteEndElement();

        foreach (var styleDefinition in styleDefinitions)
        {
            writer.WriteStartElement("font");
            if (styleDefinition.Value.IsBold)
            {
                writer.WriteStartElement("b");
                writer.WriteEndElement();
            }

            writer.WriteStartElement("sz");
            writer.WriteAttributeString("val", "11");
            writer.WriteEndElement();
            writer.WriteStartElement("color");
            writer.WriteAttributeString("rgb", ToArgb(styleDefinition.Value.FontColorHex));
            writer.WriteEndElement();
            writer.WriteStartElement("name");
            writer.WriteAttributeString("val", DefaultFontName);
            writer.WriteEndElement();
            writer.WriteStartElement("family");
            writer.WriteAttributeString("val", "2");
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteFills(XmlWriter writer, IReadOnlyList<KeyValuePair<string, CellStyle>> styleDefinitions)
    {
        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", (styleDefinitions.Count + 2).ToString());

        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", "none");
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", "gray125");
        writer.WriteEndElement();
        writer.WriteEndElement();

        foreach (var styleDefinition in styleDefinitions)
        {
            writer.WriteStartElement("fill");
            writer.WriteStartElement("patternFill");
            writer.WriteAttributeString("patternType", "solid");
            writer.WriteStartElement("fgColor");
            writer.WriteAttributeString("rgb", ToArgb(styleDefinition.Value.FillColorHex));
            writer.WriteEndElement();
            writer.WriteStartElement("bgColor");
            writer.WriteAttributeString("indexed", "64");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteBorders(XmlWriter writer)
    {
        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border");
        writer.WriteElementString("left", string.Empty);
        writer.WriteElementString("right", string.Empty);
        writer.WriteElementString("top", string.Empty);
        writer.WriteElementString("bottom", string.Empty);
        writer.WriteElementString("diagonal", string.Empty);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellStyleXfs(XmlWriter writer)
    {
        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", "0");
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("applyAlignment", "1");
        WriteCenterAlignment(writer);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellXfs(XmlWriter writer, IReadOnlyList<KeyValuePair<string, CellStyle>> styleDefinitions)
    {
        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", (styleDefinitions.Count + 1).ToString());

        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", "0");
        writer.WriteAttributeString("fillId", "0");
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("applyAlignment", "1");
        WriteCenterAlignment(writer);
        writer.WriteEndElement();

        for (var index = 0; index < styleDefinitions.Count; index++)
        {
            writer.WriteStartElement("xf");
            writer.WriteAttributeString("numFmtId", "0");
            writer.WriteAttributeString("fontId", (index + 1).ToString());
            writer.WriteAttributeString("fillId", (index + 2).ToString());
            writer.WriteAttributeString("borderId", "0");
            writer.WriteAttributeString("xfId", "0");
            writer.WriteAttributeString("applyFont", "1");
            writer.WriteAttributeString("applyFill", "1");
            writer.WriteAttributeString("applyAlignment", "1");
            WriteCenterAlignment(writer);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteCellStyles(XmlWriter writer)
    {
        writer.WriteStartElement("cellStyles");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle");
        writer.WriteAttributeString("name", "Normal");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("builtinId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCenterAlignment(XmlWriter writer)
    {
        writer.WriteStartElement("alignment");
        writer.WriteAttributeString("horizontal", "center");
        writer.WriteEndElement();
    }

    private static void WriteDataValidations(XmlWriter writer, IReadOnlyList<DataValidation> dataValidations)
    {
        writer.WriteStartElement("dataValidations");
        writer.WriteAttributeString("count", dataValidations.Count.ToString());

        foreach (var dataValidation in dataValidations)
        {
            writer.WriteStartElement("dataValidation");
            writer.WriteAttributeString("type", dataValidation.Type);
            writer.WriteAttributeString("allowBlank", dataValidation.AllowBlank ? "1" : "0");
            writer.WriteAttributeString("sqref", dataValidation.Sqref);
            writer.WriteStartElement("formula1");
            writer.WriteString(dataValidation.Formula1);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteRow(
        XmlWriter writer,
        int rowIndex,
        IReadOnlyList<CellData> values,
        IReadOnlyDictionary<string, int> styleIndexByKey)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString());

        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            var cell = values[columnIndex];
            var cellReference = GetColumnName(columnIndex + 1) + rowIndex;
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", cellReference);

            if (!string.IsNullOrWhiteSpace(cell.StyleKey) &&
                styleIndexByKey.TryGetValue(cell.StyleKey, out var styleIndex))
            {
                writer.WriteAttributeString("s", styleIndex.ToString());
            }

            switch (cell.ValueKind)
            {
                case CellValueKind.Text:
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteStartElement("is");
                    writer.WriteStartElement("t");

                    var sanitizedValue = SanitizeForXml(cell.Value);
                    if (!string.Equals(sanitizedValue, sanitizedValue.Trim(), StringComparison.Ordinal))
                    {
                        writer.WriteAttributeString("xml", "space", null, "preserve");
                    }

                    writer.WriteString(sanitizedValue);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    break;

                case CellValueKind.Number:
                    writer.WriteElementString("v", cell.Value);
                    break;

                case CellValueKind.FormulaNumber:
                    if (string.IsNullOrWhiteSpace(cell.Formula))
                    {
                        throw new InvalidOperationException("Formula cells must provide a formula.");
                    }

                    writer.WriteElementString("f", cell.Formula);
                    writer.WriteElementString("v", cell.Value);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported cell value kind '{cell.ValueKind}'.");
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static Dictionary<string, int> BuildStyleIndexByKey(IReadOnlyList<KeyValuePair<string, CellStyle>> styleDefinitions)
    {
        var styleIndexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < styleDefinitions.Count; index++)
        {
            styleIndexByKey[styleDefinitions[index].Key] = index + 1;
        }

        return styleIndexByKey;
    }

    private static string ToArgb(string hexColor)
    {
        var normalized = NormalizeHexColor(hexColor);
        return normalized.Length == 8
            ? normalized
            : $"FF{normalized}";
    }

    private static string NormalizeHexColor(string hexColor)
    {
        var normalized = hexColor.Trim().TrimStart('#').ToUpperInvariant();

        if ((normalized.Length != 6 && normalized.Length != 8) ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException($"Invalid hex color '{hexColor}'.", nameof(hexColor));
        }

        return normalized;
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
