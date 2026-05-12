using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TechNormBlazor.Data.Seeder;

public static class TestPdfGenerator
{
    private static readonly (string CaseId, string Operation, string Date, int Shift, string Resource, double DurMin, int Qty)[] Rows =
    [
        ("CASE-2026-001", "Токарная обработка",    "07.04.2026", 1, "Токарный станок CNC-200",   44.0,  3),
        ("CASE-2026-001", "Сверление",             "07.04.2026", 1, "Сверлильный станок 2Н125",  11.5,  3),
        ("CASE-2026-001", "Термическая обработка", "07.04.2026", 1, "Печь ТВЧ",                  26.0,  3),
        ("CASE-2026-001", "Шлифовка",              "07.04.2026", 2, "Шлифовальный станок 3Б12",  39.5,  3),
        ("CASE-2026-001", "Контроль качества",     "07.04.2026", 2, "Рабочее место контролёра",   9.0,  3),
        ("CASE-2026-002", "Токарная обработка",    "09.04.2026", 1, "Токарный станок CNC-200",   47.0,  2),
        ("CASE-2026-002", "Сверление",             "09.04.2026", 1, "Сверлильный станок 2Н125",  13.0,  2),
        ("CASE-2026-002", "Шлифовка",              "09.04.2026", 2, "Шлифовальный станок 3Б12",  36.0,  2),
        ("CASE-2026-002", "Контроль качества",     "09.04.2026", 2, "Рабочее место контролёра",  10.5,  2),
        ("CASE-2026-003", "Токарная обработка",    "11.04.2026", 1, "Токарный станок CNC-200",   31.0,  5),
        ("CASE-2026-003", "Фрезерование",          "11.04.2026", 1, "Фрезерный станок VMC-500",  54.5,  5),
        ("CASE-2026-003", "Шлифовка",              "11.04.2026", 2, "Шлифовальный станок 3Б12",  21.0,  5),
        ("CASE-2026-003", "Контроль качества",     "11.04.2026", 2, "Рабочее место контролёра",   8.0,  5),
        ("CASE-2026-004", "Токарная обработка",    "14.04.2026", 3, "Токарный станок CNC-200",   33.5,  4),
        ("CASE-2026-004", "Фрезерование",          "14.04.2026", 3, "Фрезерный станок VMC-500",  57.0,  4),
        ("CASE-2026-004", "Контроль качества",     "14.04.2026", 3, "Рабочее место контролёра",   7.5,  4),
        ("CASE-2026-005", "Токарная обработка",    "18.04.2026", 1, "Токарь 4-го разряда",       46.0,  1),
        ("CASE-2026-005", "Шлифовка",              "18.04.2026", 1, "Шлифовальный станок 3Б12",  37.0,  1),
        ("CASE-2026-005", "Контроль качества",     "18.04.2026", 1, "Рабочее место контролёра",  11.0,  1),
        ("CASE-2026-006", "Фрезерование",          "21.04.2026", 2, "Фрезерный станок VMC-500",  52.0,  6),
    ];

    public static void Generate(string outputPath)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text("АС «ТехНорм» — Журнал производственных событий")
                        .FontSize(13).Bold().AlignCenter();
                    col.Item().Text("Период: апрель 2026 г. | Источник: ручной ввод | Сформировано: 08.05.2026")
                        .FontSize(8).FontColor(Colors.Grey.Darken2).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(0.5f);
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22);   // №
                        c.RelativeColumn(2.2f); // ID дела
                        c.RelativeColumn(3.0f); // Операция
                        c.RelativeColumn(1.8f); // Дата
                        c.ConstantColumn(38);   // Смена
                        c.RelativeColumn(3.5f); // Ресурс
                        c.ConstantColumn(55);   // Длит. мин
                        c.ConstantColumn(40);   // Кол-во
                    });

                    // Header row
                    static IContainer HeaderCell(IContainer c) =>
                        c.Background(Colors.Blue.Darken3)
                         .Padding(4)
                         .AlignCenter()
                         .AlignMiddle();

                    table.Header(h =>
                    {
                        foreach (var label in new[] { "№", "ID дела", "Операция", "Дата", "Смена", "Ресурс", "Длит., мин", "Кол-во" })
                            h.Cell().Element(HeaderCell).Text(label).FontColor(Colors.White).FontSize(8).Bold();
                    });

                    // Data rows
                    for (int i = 0; i < Rows.Length; i++)
                    {
                        var r = Rows[i];
                        bool odd = i % 2 == 0;
                        string bg = odd ? Colors.White : Colors.Grey.Lighten4;

                        IContainer DataCell(IContainer c) =>
                            c.Background(bg).BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2)
                             .Padding(4).AlignMiddle();

                        table.Cell().Element(DataCell).AlignCenter().Text((i + 1).ToString()).FontSize(8);
                        table.Cell().Element(DataCell).Text(r.CaseId).FontSize(8).SemiBold();
                        table.Cell().Element(DataCell).Text(r.Operation).FontSize(8);
                        table.Cell().Element(DataCell).AlignCenter().Text(r.Date).FontSize(8);
                        table.Cell().Element(DataCell).AlignCenter().Text(r.Shift.ToString()).FontSize(8);
                        table.Cell().Element(DataCell).Text(r.Resource).FontSize(8).FontColor(Colors.Grey.Darken2);
                        table.Cell().Element(DataCell).AlignCenter().Text(r.DurMin.ToString("F1")).FontSize(8);
                        table.Cell().Element(DataCell).AlignCenter().Text(r.Qty.ToString()).FontSize(8);
                    }
                });

                page.Footer().AlignRight()
                    .Text(t =>
                    {
                        t.Span("Стр. ").FontSize(8).FontColor(Colors.Grey.Darken2);
                        t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken2);
                        t.Span(" из ").FontSize(8).FontColor(Colors.Grey.Darken2);
                        t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken2);
                    });
            });
        }).GeneratePdf(outputPath);
    }
}
