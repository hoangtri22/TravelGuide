using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

static string Escape(string s) => s.Replace("\0", "");

static Paragraph Para(string text, bool bold = false, string? fontSizeHalfPoints = null, string? font = null)
{
    text = Escape(text);
    var run = new Run();
    var rp = new RunProperties();
    if (bold) rp.AppendChild(new Bold());
    if (fontSizeHalfPoints is not null) rp.AppendChild(new FontSize { Val = fontSizeHalfPoints });
    if (font is not null) rp.AppendChild(new RunFonts { Ascii = font, HighAnsi = font });
    if (rp.HasChildren) run.AppendChild(rp);
    run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    return new Paragraph(run);
}

static void AddTable(Body body, IReadOnlyList<string[]> rows)
{
    if (rows.Count == 0) return;
    var colCount = rows.Max(r => r.Length);
    var twipsPerCol = (9000 / Math.Max(colCount, 1)).ToString();
    var grid = new TableGrid(Enumerable.Range(0, colCount).Select(_ => new GridColumn { Width = twipsPerCol }).ToArray());
    var tbl = new Table(
        new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }),
        grid);

    foreach (var row in rows)
    {
        var tr = new TableRow();
        for (var c = 0; c < colCount; c++)
        {
            var cellText = c < row.Length ? row[c] : string.Empty;
            var tc = new TableCell(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = twipsPerCol }),
                new Paragraph(new Run(new Text(Escape(cellText)) { Space = SpaceProcessingModeValues.Preserve })));
            tr.AppendChild(tc);
        }
        tbl.AppendChild(tr);
    }
    body.AppendChild(tbl);
    body.AppendChild(new Paragraph());
}

static bool IsTableRow(string line) =>
    line.Contains('|', StringComparison.Ordinal) && line.TrimStart().StartsWith("|", StringComparison.Ordinal);

static string[] ParseTableRow(string line) =>
    line.Split('|', StringSplitOptions.None).Skip(1).SkipLast(1).Select(c => c.Trim()).ToArray();

var mdPath = args.Length > 0 ? args[0] : throw new InvalidOperationException("Usage: PrdDocxGen <input.md> <output.docx>");
var outPath = args.Length > 1 ? args[1] : Path.ChangeExtension(mdPath, ".docx");

var lines = File.ReadAllLines(mdPath, Encoding.UTF8);
using var stream = File.Create(outPath);
using var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true);
var mainPart = wordDoc.AddMainDocumentPart();
mainPart.Document = new Document();
var body = mainPart.Document.AppendChild(new Body());

var i = 0;
var inCode = false;
while (i < lines.Length)
{
    var line = lines[i];
    if (line.StartsWith("```", StringComparison.Ordinal))
    {
        inCode = !inCode;
        i++;
        continue;
    }

    if (inCode)
    {
        body.AppendChild(Para(line, font: "Consolas", fontSizeHalfPoints: "20"));
        i++;
        continue;
    }

    if (string.IsNullOrWhiteSpace(line))
    {
        i++;
        continue;
    }

    if (line.StartsWith("# ", StringComparison.Ordinal))
    {
        body.AppendChild(Para(line[2..].Trim(), bold: true, fontSizeHalfPoints: "36"));
        i++;
        continue;
    }

    if (line.StartsWith("## ", StringComparison.Ordinal))
    {
        body.AppendChild(Para(line[3..].Trim(), bold: true, fontSizeHalfPoints: "32"));
        i++;
        continue;
    }

    if (line.StartsWith("### ", StringComparison.Ordinal))
    {
        body.AppendChild(Para(line[4..].Trim(), bold: true, fontSizeHalfPoints: "28"));
        i++;
        continue;
    }

    if (IsTableRow(line))
    {
        var tableRows = new List<string[]>();
        while (i < lines.Length && IsTableRow(lines[i]))
        {
            var l = lines[i];
            if (l.Contains('|', StringComparison.Ordinal) && l.Contains("---", StringComparison.Ordinal))
            {
                i++;
                continue;
            }
            tableRows.Add(ParseTableRow(l));
            i++;
        }
        AddTable(body, tableRows);
        continue;
    }

    body.AppendChild(Para(line.Trim()));
    i++;
}

mainPart.Document.Save();
Console.WriteLine($"Wrote {outPath}");
