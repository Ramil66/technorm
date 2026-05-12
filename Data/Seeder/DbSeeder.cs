using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data.Models;
using System.Text.Json;

namespace TechNormBlazor.Data.Seeder;

public static class DbSeeder
{
    public static async Task SeedAsync(TechNormDbContext db, ILogger logger)
    {
        if (await db.Products.AnyAsync())
        {
            logger.LogInformation("Seed: тестовые данные уже существуют, пропуск.");
            return;
        }

        logger.LogInformation("Seed: добавление тестовых данных...");

        // ── Операции ───────────────────────────────────────────────────────────
        var ops = new[]
        {
            new Operation { Code = "ОП-001", Name = "Токарная обработка",     Description = "Обработка на токарных станках с ЧПУ" },
            new Operation { Code = "ОП-002", Name = "Фрезерование",           Description = "Объёмное фрезерование поверхностей" },
            new Operation { Code = "ОП-003", Name = "Шлифовка",               Description = "Чистовая шлифовка до Ra 0.8" },
            new Operation { Code = "ОП-004", Name = "Сверление",              Description = "Сверление и зенкование отверстий" },
            new Operation { Code = "ОП-005", Name = "Контроль качества",      Description = "ОТК: измерение геометрии и шероховатости" },
            new Operation { Code = "ОП-006", Name = "Термическая обработка",  Description = "Закалка и отпуск" },
            new Operation { Code = "ОП-007", Name = "Промывка деталей",       Description = "Промывка в ультразвуковой ванне" },
        };
        db.Operations.AddRange(ops);

        // ── Ресурсы ────────────────────────────────────────────────────────────
        var res = new[]
        {
            new Resource { Code = "ОБ-001", Name = "Токарный станок CNC-200",   Type = "equipment",   IsActive = true },
            new Resource { Code = "ОБ-002", Name = "Фрезерный станок VMC-500",  Type = "equipment",   IsActive = true },
            new Resource { Code = "ОБ-003", Name = "Шлифовальный станок 3Б12",  Type = "equipment",   IsActive = true },
            new Resource { Code = "ОБ-004", Name = "Сверлильный станок 2Н125",  Type = "equipment",   IsActive = true },
            new Resource { Code = "ОБ-005", Name = "Печь ТВЧ",                  Type = "equipment",   IsActive = true },
            new Resource { Code = "РМ-001", Name = "Рабочее место контролёра",  Type = "workstation", IsActive = true },
            new Resource { Code = "ПЕ-001", Name = "Токарь 4-го разряда",       Type = "personnel",   IsActive = true },
            new Resource { Code = "ИН-001", Name = "Микрометр МК 0-25",         Type = "tool",        IsActive = true },
        };
        db.Resources.AddRange(res);

        // ── Материалы ──────────────────────────────────────────────────────────
        var mats = new[]
        {
            new Material { Code = "МТ-001", Name = "Сталь 45 ГОСТ 1050-2013",    Unit = "кг",  Description = "Конструкционная углеродистая сталь" },
            new Material { Code = "МТ-002", Name = "Чугун СЧ20 ГОСТ 1412-85",    Unit = "кг",  Description = "Серый литейный чугун" },
            new Material { Code = "МТ-003", Name = "Смазка ЦИАТИМ-201",           Unit = "г",   Description = "Пластичная смазка для подшипников" },
            new Material { Code = "МТ-004", Name = "Алюминий АД31 ГОСТ 22233",    Unit = "кг",  Description = "Деформируемый алюминиевый сплав" },
            new Material { Code = "МТ-005", Name = "Охлаждающая жидкость ECOCUT", Unit = "л",   Description = "СОЖ для металлообработки" },
        };
        db.Materials.AddRange(mats);

        // ── Изделия ────────────────────────────────────────────────────────────
        var products = new[]
        {
            new Product { Code = "ИЗД-001", Name = "Вал редуктора Р-120",      Type = "product",       Description = "Вал выходной одноступенчатого редуктора, сталь 45" },
            new Product { Code = "ИЗД-002", Name = "Зубчатая шестерня Z=24",   Type = "semi_finished", Description = "Цилиндрическая прямозубая шестерня m=2, z=24" },
            new Product { Code = "ИЗД-003", Name = "Корпус подшипника №308",   Type = "product",       Description = "Корпус под подшипник 6308, чугун СЧ20" },
        };
        db.Products.AddRange(products);

        await db.SaveChangesAsync();

        // ── Маршрутные карты ───────────────────────────────────────────────────
        // Маршрут 1: Вал редуктора
        var route1 = new TechRoute
        {
            ProductId = products[0].Id,
            Name      = "МК — Вал редуктора Р-120",
            Version   = 1,
            Status    = "published",
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            UpdatedAt = DateTime.UtcNow.AddDays(-5),
            PublishedAt = DateTime.UtcNow.AddDays(-20),
        };
        // Маршрут 2: Шестерня
        var route2 = new TechRoute
        {
            ProductId = products[1].Id,
            Name      = "МК — Зубчатая шестерня Z=24",
            Version   = 1,
            Status    = "published",
            CreatedAt = DateTime.UtcNow.AddDays(-25),
            UpdatedAt = DateTime.UtcNow.AddDays(-3),
            PublishedAt = DateTime.UtcNow.AddDays(-18),
        };
        // Маршрут 3: Корпус (черновик)
        var route3 = new TechRoute
        {
            ProductId = products[2].Id,
            Name      = "МК — Корпус подшипника №308 (черновик)",
            Version   = 1,
            Status    = "draft",
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        db.TechRoutes.AddRange(route1, route2, route3);
        await db.SaveChangesAsync();

        // ── Шаги маршрута 1 ────────────────────────────────────────────────────
        var steps1 = new[]
        {
            new RouteStep { RouteId = route1.Id, SequenceNum = 1, OperationId = ops[0].Id, Description = "Обточка наружных поверхностей вала" },
            new RouteStep { RouteId = route1.Id, SequenceNum = 2, OperationId = ops[3].Id, Description = "Сверление центрового отверстия ∅12" },
            new RouteStep { RouteId = route1.Id, SequenceNum = 3, OperationId = ops[5].Id, Description = "Закалка ТВЧ поверхности шеек HRC 45-50" },
            new RouteStep { RouteId = route1.Id, SequenceNum = 4, OperationId = ops[2].Id, Description = "Шлифовка шеек до Ra 0.8" },
            new RouteStep { RouteId = route1.Id, SequenceNum = 5, OperationId = ops[4].Id, Description = "Контроль ОТК: биение, шероховатость" },
        };
        // ── Шаги маршрута 2 ────────────────────────────────────────────────────
        var steps2 = new[]
        {
            new RouteStep { RouteId = route2.Id, SequenceNum = 1, OperationId = ops[0].Id, Description = "Обточка заготовки шестерни" },
            new RouteStep { RouteId = route2.Id, SequenceNum = 2, OperationId = ops[1].Id, Description = "Нарезание зубьев на зубофрезерном станке" },
            new RouteStep { RouteId = route2.Id, SequenceNum = 3, OperationId = ops[2].Id, Description = "Шлифовка торцов и ступицы" },
            new RouteStep { RouteId = route2.Id, SequenceNum = 4, OperationId = ops[4].Id, Description = "Контроль: шаг и профиль зуба" },
        };
        db.RouteSteps.AddRange(steps1);
        db.RouteSteps.AddRange(steps2);
        await db.SaveChangesAsync();

        // ── Нормы времени ──────────────────────────────────────────────────────
        var timeNorms = new[]
        {
            new TimeNorm { RouteStepId = steps1[0].Id, ResourceId = res[0].Id, NormValue = 45.5m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps1[1].Id, ResourceId = res[3].Id, NormValue = 12.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps1[2].Id, ResourceId = res[4].Id, NormValue = 25.0m,  IsManual = true,  UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps1[3].Id, ResourceId = res[2].Id, NormValue = 38.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps1[4].Id, ResourceId = res[5].Id, NormValue = 10.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps2[0].Id, ResourceId = res[0].Id, NormValue = 32.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps2[1].Id, ResourceId = res[1].Id, NormValue = 55.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps2[2].Id, ResourceId = res[2].Id, NormValue = 20.0m,  IsManual = false, UpdatedAt = DateTime.UtcNow },
            new TimeNorm { RouteStepId = steps2[3].Id, ResourceId = res[5].Id, NormValue = 8.0m,   IsManual = false, UpdatedAt = DateTime.UtcNow },
        };
        db.TimeNorms.AddRange(timeNorms);

        // ── Нормы материалов ───────────────────────────────────────────────────
        var matNorms = new[]
        {
            new MaterialNorm { RouteStepId = steps1[0].Id, MaterialId = mats[0].Id, ConsumptionRate = 2.4m,   UpdatedAt = DateTime.UtcNow },
            new MaterialNorm { RouteStepId = steps1[0].Id, MaterialId = mats[4].Id, ConsumptionRate = 0.15m,  UpdatedAt = DateTime.UtcNow },
            new MaterialNorm { RouteStepId = steps1[2].Id, MaterialId = mats[2].Id, ConsumptionRate = 5.0m,   UpdatedAt = DateTime.UtcNow },
            new MaterialNorm { RouteStepId = steps2[0].Id, MaterialId = mats[0].Id, ConsumptionRate = 0.85m,  UpdatedAt = DateTime.UtcNow },
            new MaterialNorm { RouteStepId = steps2[0].Id, MaterialId = mats[4].Id, ConsumptionRate = 0.10m,  UpdatedAt = DateTime.UtcNow },
        };
        db.MaterialNorms.AddRange(matNorms);

        // ── Журнал событий ─────────────────────────────────────────────────────
        var baseDate = DateTime.UtcNow.AddDays(-14);
        var events = new List<EventLog>();

        var eventData = new[]
        {
            // (caseId, activity, productIdx, resourceIdx, durationMin, shift, qty)
            ("CASE-2026-001", "Токарная обработка", 0, 0, 44.0, 1, 3),
            ("CASE-2026-001", "Сверление",          0, 3, 11.5, 1, 3),
            ("CASE-2026-001", "Термическая обработка", 0, 4, 26.0, 1, 3),
            ("CASE-2026-001", "Шлифовка",           0, 2, 39.5, 2, 3),
            ("CASE-2026-001", "Контроль качества",  0, 5,  9.0, 2, 3),

            ("CASE-2026-002", "Токарная обработка", 0, 0, 47.0, 1, 2),
            ("CASE-2026-002", "Сверление",          0, 3, 13.0, 1, 2),
            ("CASE-2026-002", "Шлифовка",           0, 2, 36.0, 2, 2),
            ("CASE-2026-002", "Контроль качества",  0, 5, 10.5, 2, 2),

            ("CASE-2026-003", "Токарная обработка", 1, 0, 31.0, 1, 5),
            ("CASE-2026-003", "Фрезерование",       1, 1, 54.5, 1, 5),
            ("CASE-2026-003", "Шлифовка",           1, 2, 21.0, 2, 5),
            ("CASE-2026-003", "Контроль качества",  1, 5,  8.0, 2, 5),

            ("CASE-2026-004", "Токарная обработка", 1, 0, 33.5, 3, 4),
            ("CASE-2026-004", "Фрезерование",       1, 1, 57.0, 3, 4),
            ("CASE-2026-004", "Контроль качества",  1, 5,  7.5, 3, 4),

            ("CASE-2026-005", "Токарная обработка", 0, 6, 46.0, 1, 1),
            ("CASE-2026-005", "Шлифовка",           0, 2, 37.0, 1, 1),
            ("CASE-2026-005", "Контроль качества",  0, 5, 11.0, 1, 1),

            ("CASE-2026-006", "Фрезерование",       1, 1, 52.0, 2, 6),
        };

        for (int i = 0; i < eventData.Length; i++)
        {
            var (caseId, activity, pIdx, rIdx, dur, shift, qty) = eventData[i];
            var meta = JsonDocument.Parse(JsonSerializer.Serialize(new { Shift = shift, Quantity = qty, Notes = (string?)null, OperationId = (int?)null }));
            events.Add(new EventLog
            {
                CaseId     = caseId,
                Activity   = activity,
                Timestamp  = baseDate.AddDays(i / 5).AddHours(8 + (i % 8)),
                ProductId  = products[pIdx].Id,
                ResourceId = res[rIdx].Id,
                Duration   = TimeSpan.FromMinutes(dur),
                Source     = "manual",
                Metadata   = meta,
                CreatedAt  = DateTime.UtcNow.AddDays(-13 + i / 5),
            });
        }
        db.EventLogs.AddRange(events);

        // ── История расчётов ───────────────────────────────────────────────────
        var calcMetrics1 = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            TotalTimeMin   = 130.0,
            MaterialsKg    = 2.55,
            EfficiencyPct  = 87.4,
        }));
        var calcMetrics2 = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            TotalTimeMin   = 115.5,
            MaterialsKg    = 0.95,
            EfficiencyPct  = 91.2,
        }));
        db.CalculationHistories.AddRange(
            new CalculationHistory
            {
                ProductId     = products[0].Id,
                RouteId       = route1.Id,
                CalculatedAt  = DateTime.UtcNow.AddDays(-5),
                Metrics       = calcMetrics1,
            },
            new CalculationHistory
            {
                ProductId     = products[1].Id,
                RouteId       = route2.Id,
                CalculatedAt  = DateTime.UtcNow.AddDays(-2),
                Metrics       = calcMetrics2,
            }
        );

        await db.SaveChangesAsync();
        logger.LogInformation("Seed: добавлено {Ops} операций, {Res} ресурсов, {Mat} материалов, " +
                              "{Prod} изделий, {Routes} маршрутов, {Steps} шагов, {Events} событий.",
            ops.Length, res.Length, mats.Length, products.Length, 3,
            steps1.Length + steps2.Length, events.Count);
    }
}
