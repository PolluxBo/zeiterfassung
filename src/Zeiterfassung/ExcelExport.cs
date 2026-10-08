using ClosedXML.Excel;

namespace Zeiterfassung;

public static class ExcelExport
{
    // Minuten als Excel-Zeitwert; der winzige Zuschlag verhindert Anzeigen wie 1:59 statt 2:00 durch Rundungsfehler.
    static double Days(int minutes) => minutes / 1440.0 + 1e-9;

    public static void Write(string path, IReadOnlyList<string> keys, Store store, string period)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Zeiterfassung");

        ws.Cell(1, 1).Value = "Zeiterfassung";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Mitarbeiter";
        ws.Cell(2, 2).Value = store.Settings.Name;
        ws.Cell(3, 1).Value = "Zeitraum";
        ws.Cell(3, 2).Value = period;

        const int headerRow = 5;
        string[] headers = { "Datum", "Wochentag", "Kunde", "Tätigkeit / Notiz", "Dauer (h:mm)", "Stunden" };
        for (int c = 0; c < headers.Length; c++) ws.Cell(headerRow, c + 1).Value = headers[c];
        var header = ws.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEAF4");
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        int first = headerRow + 1, row = first, total = 0;
        var perKunde = new Dictionary<string, int>();
        foreach (var key in keys)
        {
            var d = D.FromKey(key);
            foreach (var e in store.Day(key))
            {
                total += e.Min;
                perKunde[e.Kunde] = perKunde.TryGetValue(e.Kunde, out var m) ? m + e.Min : e.Min;
                ws.Cell(row, 1).Value = d;
                ws.Cell(row, 2).Value = D.WdLong(d);
                ws.Cell(row, 3).Value = e.Kunde;
                ws.Cell(row, 4).Value = e.Notiz ?? "";
                ws.Cell(row, 5).Value = Days(e.Min);
                ws.Cell(row, 6).Value = Math.Round(e.Min / 60.0, 2);
                row++;
            }
        }
        int last = row - 1;

        int sumRow = last + 2;
        ws.Cell(sumRow, 4).Value = "Summe";
        ws.Cell(sumRow, 5).FormulaA1 = $"SUM(E{first}:E{last})";
        ws.Cell(sumRow, 6).FormulaA1 = $"SUM(F{first}:F{last})";
        ws.Range(sumRow, 4, sumRow, 6).Style.Font.Bold = true;
        ws.Range(sumRow, 4, sumRow, 6).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        int kRow = sumRow + 2;
        ws.Cell(kRow, 1).Value = "Summe je Kunde";
        ws.Cell(kRow, 1).Style.Font.Bold = true;
        foreach (var (kunde, m) in perKunde.OrderByDescending(p => p.Value))
        {
            kRow++;
            ws.Cell(kRow, 3).Value = kunde;
            ws.Cell(kRow, 5).Value = Days(m);
            ws.Cell(kRow, 6).Value = Math.Round(m / 60.0, 2);
        }

        ws.Range(first, 1, Math.Max(first, kRow), 1).Style.NumberFormat.Format = "dd.mm.yyyy";
        ws.Range(first, 5, Math.Max(first, kRow), 5).Style.NumberFormat.Format = "[h]:mm";
        ws.Range(first, 6, Math.Max(first, kRow), 6).Style.NumberFormat.Format = "0.00";

        ws.Column(1).Width = 12;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 30;
        ws.Column(4).Width = 44;
        ws.Column(5).Width = 13;
        ws.Column(6).Width = 10;
        ws.Column(4).Style.Alignment.WrapText = true;
        if (last >= first) ws.Range(headerRow, 1, last, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);

        wb.SaveAs(path);
    }
}
