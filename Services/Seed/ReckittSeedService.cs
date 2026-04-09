using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Panwar.Api.Data;
using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services.Seed;

/// <summary>
/// One-shot Reckitt workbook → DB importer (Phase B scope).
///
/// What this seeder covers:
/// - Reckitt client + branding
/// - 3 brands (Nurofen, NFC, Gaviscon) × 2 audiences (Pharmacists, GPs) = 6 brand×audience combos
/// - All 10 publishers as global catalog
/// - 6 metric templates with their fields
/// - publisher_template mappings
/// - All placements from the YTD Data sheet, both DIGITAL and PRINT sections,
///   handling Impressions / Views / Page Views block types (digital banners,
///   eDMs, sponsored content, education courses)
/// - Per-month actuals + per-placement KPIs
///
/// Still out of scope (Phase C):
/// - Education sheets (Pharmacist Education / GP Education / Pharmacy Education tabs)
///   — these track CPD course completions over multi-year timelines and are
///   parsed differently from the YTD Data sheet
/// - UTM URL Tracking sheet (utm_link + monthly clicks)
/// - OS codes / objective tags / long-form comments / artwork URLs from the
///   brand-audience tab cards (the cards have richer metadata than YTD Data)
/// - **Organic views of sponsored content** — the YTD Data sheet only has paid
///   views; organic views live on the brand-audience tab cards. This causes a
///   small impression-count discrepancy on the GP tabs vs the workbook OVERVIEW
///   numbers (Pharmacist tabs match exactly because they have no sponsored content).
/// - Uploading the 137 embedded artwork images to R2 and linking them to placements
///
/// This service is NOT production-ready importer code. It's a one-off seeder
/// that runs to populate dev/staging databases with real Reckitt data so we
/// have a validation target for building the dashboards. The Phase 3 publisher
/// actuals importers will be different code paths (per-publisher monthly exports,
/// not this aggregated master workbook).
/// </summary>
public class ReckittSeedService : IReckittSeedService
{
    private const string ReckittSlug = "reckitt";

    private readonly AppDbContext _context;
    private readonly ILogger<ReckittSeedService> _logger;

    public ReckittSeedService(AppDbContext context, ILogger<ReckittSeedService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SeedSummary> SeedAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException($"Reckitt workbook not found at {workbookPath}");

        _logger.LogInformation("Starting Reckitt seed from {Path}", workbookPath);
        var summary = new SeedSummary();

        using var workbook = new XLWorkbook(workbookPath);

        await DeleteExistingReckittDataAsync(cancellationToken);

        var (client, brands, audiences) = await SeedTenancyAsync(cancellationToken);
        summary.Brands = brands.Count;
        summary.Audiences = audiences.Count;

        var publishers = await SeedPublishersAsync(cancellationToken);
        summary.Publishers = publishers.Count;

        var (templates, fields) = await SeedMetricTemplatesAsync(cancellationToken);
        summary.MetricTemplates = templates.Count;
        summary.MetricFields = fields;

        await SeedPublisherTemplatesAsync(publishers, templates, cancellationToken);

        // Scan YTD Data sheet to find every (brand, audience, section) range,
        // then parse each one. This is the heart of Phase B — covering all 6
        // brand×audience combos for both DIGITAL and PRINT sections.
        var ws = workbook.Worksheet("YTD Data");
        var sections = ScanYtdSections(ws);
        _logger.LogInformation("YTD Data scan found {Count} sections", sections.Count);

        var brandLookup = brands.ToDictionary(b => b.Slug);
        var audienceLookup = audiences.ToDictionary(a => a.Slug);

        foreach (var section in sections)
        {
            if (!brandLookup.TryGetValue(section.BrandSlug, out var brand))
            {
                summary.Warnings.Add($"YTD Data: section at row {section.StartRow} references unknown brand '{section.BrandSlug}'");
                continue;
            }
            if (!audienceLookup.TryGetValue(section.AudienceSlug, out var audience))
            {
                summary.Warnings.Add($"YTD Data: section at row {section.StartRow} references unknown audience '{section.AudienceSlug}'");
                continue;
            }

            var added = section.SectionType switch
            {
                YtdSectionType.Digital => ParseDigitalSection(ws, section, brand, audience, publishers, templates, summary),
                YtdSectionType.Print => ParsePrintSection(ws, section, brand, audience, publishers, templates, summary),
                _ => 0
            };
            summary.Placements += added;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Reckitt seed complete: {Summary}", System.Text.Json.JsonSerializer.Serialize(summary));
        return summary;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 1: clear any existing Reckitt data so the seed is idempotent
    // ─────────────────────────────────────────────────────────────────────

    private async Task DeleteExistingReckittDataAsync(CancellationToken cancellationToken)
    {
        // Delete in dependency order. Anything keyed by client_id (directly or
        // through brand/audience) gets wiped. Publishers + metric templates +
        // metric fields are global catalog and stay.
        _logger.LogInformation("Deleting any existing Reckitt data");

        await _context.PlacementActuals
            .Where(a => a.Placement.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.PlacementKpis
            .Where(k => k.Placement.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.PlacementComments
            .Where(c => c.Placement.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Placements
            .Where(p => p.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.EducationCourseStatuses
            .Where(s => s.Course.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.EducationCourses
            .Where(c => c.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.UtmLinkClicks
            .Where(c => c.UtmLink.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.UtmLinks
            .Where(u => u.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.MonthSnapshots
            .Where(s => s.Brand.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.ClientPublisherBaselines
            .Where(b => b.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Brands
            .Where(b => b.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Audiences
            .Where(a => a.Client.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Clients
            .Where(c => c.Slug == ReckittSlug)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 2: seed Reckitt + brands + audiences
    // ─────────────────────────────────────────────────────────────────────

    private async Task<(Client client, List<Brand> brands, List<Audience> audiences)> SeedTenancyAsync(CancellationToken cancellationToken)
    {
        var client = new Client
        {
            Id = Guid.NewGuid(),
            Slug = ReckittSlug,
            Name = "Reckitt",
            // Reckitt's brand colour (corporate red). Editor can update later.
            PrimaryColor = "#D71920",
            AccentColor = "#454646",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Clients.Add(client);

        var brands = new List<Brand>
        {
            new() { Id = Guid.NewGuid(), ClientId = client.Id, Slug = "nurofen", Name = "Nurofen", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ClientId = client.Id, Slug = "nfc", Name = "Nurofen for Children", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ClientId = client.Id, Slug = "gaviscon", Name = "Gaviscon", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        _context.Brands.AddRange(brands);

        var audiences = new List<Audience>
        {
            new() { Id = Guid.NewGuid(), ClientId = client.Id, Slug = "pharmacists", Name = "Pharmacists", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ClientId = client.Id, Slug = "gps", Name = "GPs", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        _context.Audiences.AddRange(audiences);

        await _context.SaveChangesAsync(cancellationToken);
        return (client, brands, audiences);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 3: seed publishers (global catalog — only inserts missing ones)
    // ─────────────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, Publisher>> SeedPublishersAsync(CancellationToken cancellationToken)
    {
        // 10 publishers Panwar Health currently runs media with.
        // Slug is the lookup key used by the placement parser.
        var catalog = new (string Slug, string Name)[]
        {
            ("ajp", "Australian Journal of Pharmacy"),
            ("ap", "Australian Pharmacist"),
            ("arterial", "Arterial"),
            ("healthed", "Healthed"),
            ("ajgp", "Australian Journal of General Practice"),
            ("adg", "Australian Doctor Group"),
            ("princeton", "Princeton"),
            ("newsgp", "NewsGP"),
            ("medical-today", "Medical Today"),
            ("praxhub", "Praxhub")
        };

        var existing = await _context.Publishers
            .Where(p => catalog.Select(c => c.Slug).Contains(p.Slug))
            .ToDictionaryAsync(p => p.Slug, cancellationToken);

        foreach (var (slug, name) in catalog)
        {
            if (existing.ContainsKey(slug)) continue;
            var publisher = new Publisher
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Publishers.Add(publisher);
            existing[slug] = publisher;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 4: seed metric templates + their fields
    // ─────────────────────────────────────────────────────────────────────

    private async Task<(Dictionary<MetricTemplateCode, MetricTemplate> templates, int fieldCount)> SeedMetricTemplatesAsync(CancellationToken cancellationToken)
    {
        var existing = await _context.MetricTemplates
            .Include(t => t.Fields)
            .ToDictionaryAsync(t => t.Code, cancellationToken);

        var defs = new (MetricTemplateCode Code, string Name, (string Key, string Label, string? Unit, bool Calculated, string? Formula)[] Fields)[]
        {
            (MetricTemplateCode.DigitalDisplay, "Digital Display", new[]
            {
                ("impressions", "Impressions", (string?)null, false, (string?)null),
                ("clicks", "Clicks", null, false, null),
                ("ctr", "CTR", "%", true, "clicks / impressions"),
                ("media_cost", "Media Cost", "AUD", false, null),
                ("cpm", "CPM", "AUD", true, "media_cost / impressions * 1000"),
                ("cpc", "CPC", "AUD", true, "media_cost / clicks"),
            }),
            (MetricTemplateCode.Edm, "eDM", new[]
            {
                ("sends", "Sends", (string?)null, false, (string?)null),
                ("opens", "Opens", null, false, null),
                ("open_rate", "Open Rate", "%", true, "opens / sends"),
                ("clicks", "Clicks", null, false, null),
                ("ctr", "CTR", "%", true, "clicks / opens"),
                ("media_cost", "Media Cost", "AUD", false, null),
            }),
            (MetricTemplateCode.Print, "Print", new[]
            {
                ("circulation", "Circulation", (string?)null, false, (string?)null),
                ("placements_count", "Placements", null, false, null),
                ("impressions", "Impressions", null, true, "circulation * placements_count"),
                ("media_cost", "Media Cost", "AUD", false, null),
            }),
            (MetricTemplateCode.SponsoredContent, "Sponsored Content", new[]
            {
                ("views", "Views", (string?)null, false, (string?)null),
                ("organic_views", "Organic Views", null, false, null),
                ("downloads", "Downloads", null, false, null),
                ("media_cost", "Media Cost", "AUD", false, null),
                ("cpv", "CPV", "AUD", true, "media_cost / views"),
            }),
            (MetricTemplateCode.EducationVideo, "Education Video", new[]
            {
                ("views", "Views", (string?)null, false, (string?)null),
                ("organic_views", "Organic Views", null, false, null),
                ("media_cost", "Media Cost", "AUD", false, null),
            }),
            (MetricTemplateCode.EducationCourse, "Education Course", new[]
            {
                ("completions", "Completions", (string?)null, false, (string?)null),
                ("pending", "Pending", null, false, null),
                ("total", "Total", null, true, "completions + pending"),
                ("completion_rate", "Completion Rate", "%", true, "completions / total"),
                ("page_views", "Page Views", null, false, null),
                ("media_cost", "Media Cost", "AUD", false, null),
            }),
        };

        var fieldCount = 0;
        foreach (var def in defs)
        {
            if (!existing.TryGetValue(def.Code, out var template))
            {
                template = new MetricTemplate
                {
                    Id = Guid.NewGuid(),
                    Code = def.Code,
                    Name = def.Name
                };
                _context.MetricTemplates.Add(template);
                existing[def.Code] = template;
            }

            // Skip if fields already seeded for this template
            if (template.Fields.Count > 0)
            {
                fieldCount += template.Fields.Count;
                continue;
            }

            for (var i = 0; i < def.Fields.Length; i++)
            {
                var (key, label, unit, calc, formula) = def.Fields[i];
                _context.MetricFields.Add(new MetricField
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    Key = key,
                    Label = label,
                    Unit = unit,
                    IsCalculated = calc,
                    CalcFormula = formula,
                    SortOrder = i
                });
                fieldCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return (existing, fieldCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 5: link publishers to the templates they offer (M:N)
    // ─────────────────────────────────────────────────────────────────────

    private async Task SeedPublisherTemplatesAsync(
        Dictionary<string, Publisher> publishers,
        Dictionary<MetricTemplateCode, MetricTemplate> templates,
        CancellationToken cancellationToken)
    {
        var mappings = new (string PublisherSlug, MetricTemplateCode[] Templates)[]
        {
            ("ajp",          new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.Edm, MetricTemplateCode.Print, MetricTemplateCode.EducationCourse }),
            ("ap",           new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.Edm, MetricTemplateCode.Print, MetricTemplateCode.EducationCourse }),
            ("arterial",     new[] { MetricTemplateCode.SponsoredContent, MetricTemplateCode.Edm, MetricTemplateCode.EducationVideo }),
            ("healthed",     new[] { MetricTemplateCode.SponsoredContent, MetricTemplateCode.EducationVideo }),
            ("ajgp",         new[] { MetricTemplateCode.Print }),
            ("adg",          new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.Edm, MetricTemplateCode.SponsoredContent }),
            ("princeton",    new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.Edm }),
            ("newsgp",       new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.Edm }),
            ("medical-today", new[] { MetricTemplateCode.DigitalDisplay }),
            ("praxhub",      new[] { MetricTemplateCode.DigitalDisplay, MetricTemplateCode.EducationCourse }),
        };

        var existingPairs = await _context.PublisherTemplates
            .Select(pt => new { pt.PublisherId, pt.TemplateId })
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<(Guid, Guid)>(existingPairs.Select(p => (p.PublisherId, p.TemplateId)));

        foreach (var (slug, codes) in mappings)
        {
            if (!publishers.TryGetValue(slug, out var publisher)) continue;
            foreach (var code in codes)
            {
                var template = templates[code];
                if (existingSet.Contains((publisher.Id, template.Id))) continue;
                _context.PublisherTemplates.Add(new PublisherTemplate
                {
                    PublisherId = publisher.Id,
                    TemplateId = template.Id
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 6: scan the YTD Data sheet to discover every (brand, audience,
    //         section) range that needs parsing. Returns one record per
    //         contiguous run of blocks belonging to the same brand-audience
    //         under the same section type.
    // ─────────────────────────────────────────────────────────────────────

    private enum YtdSectionType { Digital, Print }

    private record YtdSection(
        YtdSectionType SectionType,
        string BrandSlug,
        string AudienceSlug,
        int StartRow);

    /// <summary>
    /// Walks the YTD Data sheet column B looking for SECTION markers
    /// (DIGITAL / PRINT), AUDIENCE markers (PHARMACISTS / GPs), and brand
    /// header rows (Nurofen / NFC / Gaviscon with a metric label in col C).
    /// Returns one YtdSection per contiguous brand-audience-section run.
    ///
    /// Digital sections have a header row before EVERY block; the StartRow
    /// is the first header row.
    ///
    /// Print sections have ONE header row at the top of the run; the
    /// StartRow is that header row, and the first summary is at StartRow+1.
    /// </summary>
    private List<YtdSection> ScanYtdSections(IXLWorksheet ws)
    {
        var sections = new List<YtdSection>();
        YtdSectionType? currentSection = null;
        string? currentAudienceSlug = null;
        string? currentBrandKey = null;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (var r = 1; r <= lastRow; r++)
        {
            var b = ReadString(ws.Cell(r, 2));
            if (b is null) continue;

            // Top-level section marker
            if (string.Equals(b, "DIGITAL", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = YtdSectionType.Digital;
                currentAudienceSlug = null;
                currentBrandKey = null;
                continue;
            }
            if (string.Equals(b, "PRINT", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = YtdSectionType.Print;
                currentAudienceSlug = null;
                currentBrandKey = null;
                continue;
            }
            if (string.Equals(b, "EDUCATION", StringComparison.OrdinalIgnoreCase))
            {
                // Education section is parsed separately (Phase C)
                currentSection = null;
                continue;
            }

            // Audience marker
            if (string.Equals(b, "PHARMACISTS", StringComparison.OrdinalIgnoreCase))
            {
                currentAudienceSlug = "pharmacists";
                currentBrandKey = null;
                continue;
            }
            if (string.Equals(b, "GPS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(b, "GPs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(b, "GP", StringComparison.OrdinalIgnoreCase))
            {
                currentAudienceSlug = "gps";
                currentBrandKey = null;
                continue;
            }

            // Brand header row: brand name in col B + recognised metric label in col C
            var brandSlug = TryBrandSlug(b);
            if (brandSlug is null) continue;
            if (currentSection is null || currentAudienceSlug is null) continue;

            var c = ReadString(ws.Cell(r, 3));
            if (!IsKnownMetricLabel(c)) continue;

            // Open a new section ONLY when the brand changes within the current
            // (section, audience). Subsequent header rows for the same brand are
            // additional blocks of the same section, not new sections.
            var sectionKey = $"{currentSection}|{currentAudienceSlug}|{brandSlug}";
            if (currentBrandKey == sectionKey) continue;

            currentBrandKey = sectionKey;
            sections.Add(new YtdSection(currentSection.Value, brandSlug, currentAudienceSlug, r));
        }

        return sections;
    }

    private static string? TryBrandSlug(string text) => text switch
    {
        "Nurofen" => "nurofen",
        "NFC" => "nfc",
        "Gaviscon" => "gaviscon",
        _ => null
    };

    private static readonly HashSet<string> KnownMetricLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Impressions", "Print Impressions", "Views", "Page Views", "Sends"
    };

    private static bool IsKnownMetricLabel(string? value)
        => value is not null && KnownMetricLabels.Contains(value);

    // ─────────────────────────────────────────────────────────────────────
    // Step 7: parse a digital section (14-row blocks, header before each)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses one (brand, audience) digital section. The section may contain
    /// multiple block types (Impressions blocks, Views blocks, Page Views blocks)
    /// — each block's metric label is read from the header row col C, and the
    /// placement template is chosen based on it (+ name heuristics for eDM vs banner).
    ///
    /// Block layout (14 rows, columns are identical across all metric variants):
    ///   row N+0: header (brand | primaryMetric | YTD | primaryMetricKPI | YTD | secondaryMetric | YTD | secondaryMetricKPI | YTD | rate | rate KPI | Media Cost | YTD | Note)
    ///   row N+1: summary (placement name | YTD total values across the columns)
    ///   rows N+2..N+13: months 1..12
    /// </summary>
    private int ParseDigitalSection(
        IXLWorksheet ws,
        YtdSection section,
        Brand brand,
        Audience audience,
        Dictionary<string, Publisher> publishers,
        Dictionary<MetricTemplateCode, MetricTemplate> templates,
        SeedSummary summary)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var count = 0;
        var headerRow = section.StartRow;

        while (headerRow + 13 <= lastRow)
        {
            // The header row must still be the same brand. Anything else means
            // we've walked off the end of this section.
            var headerB = ReadString(ws.Cell(headerRow, 2));
            if (TryBrandSlug(headerB ?? "") != section.BrandSlug) break;

            var primaryMetricLabel = ReadString(ws.Cell(headerRow, 3));
            var secondaryMetricLabel = ReadString(ws.Cell(headerRow, 7));
            if (!IsKnownMetricLabel(primaryMetricLabel)) break;

            var (primaryKey, secondaryKey, rateKey) = MapMetricKeys(primaryMetricLabel!, secondaryMetricLabel);

            var summaryRow = headerRow + 1;
            var name = ReadString(ws.Cell(summaryRow, 2));
            if (string.IsNullOrEmpty(name))
            {
                summary.Warnings.Add($"YTD Data row {summaryRow}: empty placement name in digital section, skipping");
                headerRow += 14;
                continue;
            }

            var publisherSlug = ResolvePublisherFromPlacementName(name);
            if (publisherSlug is null)
            {
                summary.Warnings.Add($"YTD Data row {summaryRow}: could not resolve publisher from name '{name}'");
                headerRow += 14;
                continue;
            }

            var templateCode = ChooseDigitalTemplate(name, primaryMetricLabel!);
            var placementTemplate = templates[templateCode];

            var placement = new Placement
            {
                Id = Guid.NewGuid(),
                BrandId = brand.Id,
                AudienceId = audience.Id,
                PublisherId = publishers[publisherSlug].Id,
                TemplateId = placementTemplate.Id,
                Name = name,
                Objective = InferObjectiveFromName(name),
                IsBonus = name.Contains("(BONUS)", StringComparison.OrdinalIgnoreCase)
                          || name.Contains("(Unplanned Bonus)", StringComparison.OrdinalIgnoreCase),
                IsCpdPackage = ContainsCpdPackage(name),
                MediaCost = SanitizeCost(ReadDecimal(ws.Cell(summaryRow, 13)) ?? 0m),
                LiveMonths = Array.Empty<int>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Placements.Add(placement);

            // KPI values from the summary row (YTD targets)
            var primaryKpi = ReadDecimal(ws.Cell(summaryRow, 5));
            var secondaryKpi = ReadDecimal(ws.Cell(summaryRow, 9));
            var rateKpi = ReadDecimal(ws.Cell(summaryRow, 12));

            if (primaryKpi is not null) AddKpi(placement.Id, primaryKey, primaryKpi.Value, summary);
            if (secondaryKpi is not null && secondaryKey is not null) AddKpi(placement.Id, secondaryKey, secondaryKpi.Value, summary);
            if (rateKpi is not null && rateKey is not null) AddKpi(placement.Id, rateKey, rateKpi.Value, summary);

            // Per-month rows
            const int year = 2025;
            for (var monthOffset = 0; monthOffset < 12; monthOffset++)
            {
                var monthRow = summaryRow + 1 + monthOffset;
                var month = monthOffset + 1;

                var primary = ReadDecimal(ws.Cell(monthRow, 3));
                var secondary = ReadDecimal(ws.Cell(monthRow, 7));
                var rate = ReadDecimal(ws.Cell(monthRow, 11));
                var mediaCost = SanitizeCostNullable(ReadDecimal(ws.Cell(monthRow, 13)));
                var note = NormalizeNote(ReadString(ws.Cell(monthRow, 15)));

                if (primary is { } p && p != 0m) AddActual(placement.Id, year, month, primaryKey, p, note, summary);
                if (secondary is { } s && s != 0m && secondaryKey is not null) AddActual(placement.Id, year, month, secondaryKey, s, note, summary);
                if (rate is { } r && r != 0m && rateKey is not null) AddActual(placement.Id, year, month, rateKey, r, note, summary);
                if (mediaCost is { } mc && mc != 0m) AddActual(placement.Id, year, month, "media_cost", mc, note, summary);
            }

            count++;
            headerRow += 14;
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 8: parse a print section (one header at top, then 13-row blocks
    //         back-to-back with no per-block headers)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses one (brand, audience) print section. ScanYtdSections sets
    /// StartRow to the single header row at the top; the first summary
    /// is at StartRow+1. After that, each placement is 13 rows (1 summary
    /// + 12 months) back-to-back until we hit a row that isn't a print
    /// placement (next section marker or another brand).
    ///
    /// Print column layout: B=name/month, C=Impressions, D=YTD,
    /// E=Impressions KPI, F=YTD, G=Media Cost, H=YTD, I=Note. (No clicks/CTR.)
    /// </summary>
    private int ParsePrintSection(
        IXLWorksheet ws,
        YtdSection section,
        Brand brand,
        Audience audience,
        Dictionary<string, Publisher> publishers,
        Dictionary<MetricTemplateCode, MetricTemplate> templates,
        SeedSummary summary)
    {
        var template = templates[MetricTemplateCode.Print];
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var count = 0;
        var summaryRow = section.StartRow + 1; // skip the header

        while (summaryRow + 12 <= lastRow)
        {
            var name = ReadString(ws.Cell(summaryRow, 2));
            if (string.IsNullOrEmpty(name)) break;

            // Stop conditions: any value that's not a print placement name
            // (audience marker, brand header for the next section, etc.)
            if (IsSectionTerminator(name)) break;

            var publisherSlug = ResolvePublisherFromPlacementName(name);
            if (publisherSlug is null)
            {
                summary.Warnings.Add($"YTD Data row {summaryRow}: could not resolve publisher from print name '{name}'");
                summaryRow += 13;
                continue;
            }

            var placement = new Placement
            {
                Id = Guid.NewGuid(),
                BrandId = brand.Id,
                AudienceId = audience.Id,
                PublisherId = publishers[publisherSlug].Id,
                TemplateId = template.Id,
                Name = name,
                Objective = PlacementObjective.Awareness,
                IsCpdPackage = ContainsCpdPackage(name),
                MediaCost = SanitizeCost(ReadDecimal(ws.Cell(summaryRow, 7)) ?? 0m),
                LiveMonths = Array.Empty<int>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Placements.Add(placement);

            var impressionsKpi = ReadDecimal(ws.Cell(summaryRow, 5));
            if (impressionsKpi is not null) AddKpi(placement.Id, "impressions", impressionsKpi.Value, summary);

            const int year = 2025;
            for (var monthOffset = 0; monthOffset < 12; monthOffset++)
            {
                var monthRow = summaryRow + 1 + monthOffset;
                var month = monthOffset + 1;

                var impressions = ReadDecimal(ws.Cell(monthRow, 3));
                var mediaCost = SanitizeCostNullable(ReadDecimal(ws.Cell(monthRow, 7)));
                var note = NormalizeNote(ReadString(ws.Cell(monthRow, 9)));

                if (impressions is { } imp && imp != 0m) AddActual(placement.Id, year, month, "impressions", imp, note, summary);
                if (mediaCost is { } mc && mc != 0m) AddActual(placement.Id, year, month, "media_cost", mc, note, summary);
            }

            count++;
            summaryRow += 13;
        }

        return count;
    }

    private static bool IsSectionTerminator(string value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        return value.Equals("PHARMACISTS", StringComparison.OrdinalIgnoreCase)
            || value.Equals("GPS", StringComparison.OrdinalIgnoreCase)
            || value.Equals("GPs", StringComparison.OrdinalIgnoreCase)
            || value.Equals("GP", StringComparison.OrdinalIgnoreCase)
            || value.Equals("DIGITAL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PRINT", StringComparison.OrdinalIgnoreCase)
            || value.Equals("EDUCATION", StringComparison.OrdinalIgnoreCase)
            || TryBrandSlug(value) is not null;
    }

    /// <summary>
    /// Maps the column-3 metric label and the column-7 secondary metric label
    /// to the canonical metric_keys we store. Different block shapes use
    /// different terminology (Impressions vs Views vs Page Views; Clicks vs
    /// Completions) but they all live in the same column positions.
    /// </summary>
    private static (string Primary, string? Secondary, string? Rate) MapMetricKeys(string primary, string? secondary) => (
        primary.ToLowerInvariant() switch
        {
            "impressions" => "impressions",
            "print impressions" => "impressions",
            "views" => "views",
            "page views" => "page_views",
            "sends" => "sends",
            _ => "impressions"
        },
        secondary?.ToLowerInvariant() switch
        {
            "clicks" => "clicks",
            "completions" => "completions",
            "opens" => "opens",
            null => null,
            _ => null
        },
        // Rate column (col 11) — CTR for impressions/clicks, completion rate for completions
        secondary?.ToLowerInvariant() switch
        {
            "clicks" => "ctr",
            "completions" => "completion_rate",
            "opens" => "open_rate",
            _ => null
        });

    /// <summary>
    /// Choose the most-fitting metric template for a digital placement.
    /// Heuristics in order:
    ///   - Page Views block → education_course (Healthed-style sponsored articles)
    ///   - Views block → sponsored_content (Arterial-style)
    ///   - "eDM" in name → edm (covers Solus eDMs and "eDM Banner Ads")
    ///   - Otherwise → digital_display
    /// </summary>
    private static MetricTemplateCode ChooseDigitalTemplate(string placementName, string primaryMetricLabel)
    {
        if (string.Equals(primaryMetricLabel, "Page Views", StringComparison.OrdinalIgnoreCase))
            return MetricTemplateCode.EducationCourse;
        if (string.Equals(primaryMetricLabel, "Views", StringComparison.OrdinalIgnoreCase))
            return MetricTemplateCode.SponsoredContent;
        if (placementName.Contains("eDM", StringComparison.OrdinalIgnoreCase))
            return MetricTemplateCode.Edm;
        return MetricTemplateCode.DigitalDisplay;
    }

    private static bool ContainsCpdPackage(string name)
        => name.Contains("CPD Package", StringComparison.OrdinalIgnoreCase)
        || name.Contains("(CPD package)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maria's spreadsheet has formula bugs that accumulate floating-point
    /// noise into the YTD media_cost column (e.g. $4500 grows to $4500.0008
    /// over 12 months because each "0" addition is actually 0.0001). Anything
    /// less than one cent is treated as zero.
    /// </summary>
    private static decimal SanitizeCost(decimal value) => value < 0.01m ? 0m : value;

    private static decimal? SanitizeCostNullable(decimal? value)
        => value is null ? null : SanitizeCost(value.Value);

    private static string? NormalizeNote(string? note)
        => note is null or "0" ? null : note;

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void AddKpi(Guid placementId, string metricKey, decimal value, SeedSummary summary)
    {
        _context.PlacementKpis.Add(new PlacementKpi
        {
            Id = Guid.NewGuid(),
            PlacementId = placementId,
            MetricKey = metricKey,
            TargetValue = value
        });
        summary.PlacementKpis++;
    }

    private void AddActual(Guid placementId, int year, int month, string metricKey, decimal value, string? note, SeedSummary summary)
    {
        _context.PlacementActuals.Add(new PlacementActual
        {
            Id = Guid.NewGuid(),
            PlacementId = placementId,
            Year = year,
            Month = month,
            MetricKey = metricKey,
            Value = value,
            Note = note
        });
        summary.PlacementActuals++;
    }

    /// <summary>
    /// Map a placement name like "AJP Daily Banner - Mini Caps w/ CTA to Portal"
    /// or "AP eDM SC - Tolerability CPD" to the canonical publisher slug.
    /// </summary>
    private static string? ResolvePublisherFromPlacementName(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper.StartsWith("AJP")) return "ajp";
        if (upper.StartsWith("AP ") || upper.StartsWith("AP-") || upper == "AP") return "ap";
        if (upper.StartsWith("ARTERIAL")) return "arterial";
        if (upper.StartsWith("HEALTHED")) return "healthed";
        // AJGP is the journal of RACGP — Maria sometimes uses the parent org name
        if (upper.StartsWith("AJGP") || upper.StartsWith("RACGP")) return "ajgp";
        if (upper.StartsWith("ADG")) return "adg";
        if (upper.StartsWith("PRINCETON")) return "princeton";
        if (upper.StartsWith("NEWSGP")) return "newsgp";
        if (upper.StartsWith("MT ") || upper.StartsWith("MEDICAL TODAY")) return "medical-today";
        if (upper.StartsWith("PRAXHUB")) return "praxhub";
        return null;
    }

    /// <summary>
    /// Best-effort objective inference from the placement name. The brand-audience
    /// tab has explicit objective tags in the comments cell, but Phase A only reads
    /// YTD Data which doesn't have them — so we fall back to name heuristics.
    /// Phase B will read the brand-audience tab cards for the authoritative tag.
    /// </summary>
    private static PlacementObjective InferObjectiveFromName(string name)
    {
        var lower = name.ToLowerInvariant();
        // Anything that drives traffic to a CPD article/podcast is "engagement"
        if (lower.Contains("cpd") || lower.Contains("article") || lower.Contains("webinar") || lower.Contains("podcast"))
            return PlacementObjective.Engagement;
        // CTAs to brand portals are "consideration"
        if (lower.Contains("cta to") || lower.Contains("cta -"))
            return PlacementObjective.Consideration;
        // Daily banners and standalone display = awareness
        return PlacementObjective.Awareness;
    }

    private static string? ReadString(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsBlank) return null;
        var text = value.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static decimal? ReadDecimal(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsBlank) return null;
        if (value.IsNumber) return (decimal)value.GetNumber();
        if (value.IsText)
        {
            var text = value.GetText().Trim();
            if (string.IsNullOrEmpty(text) || text == "-") return null;
            if (decimal.TryParse(text, out var d)) return d;
        }
        return null;
    }
}
