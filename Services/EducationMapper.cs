using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

internal static class EducationMapper
{
    private const string OtherBrand = "Other";

    public static EducationPageResponse Build(
        EducationPage page, IReadOnlyList<Brand> clientBrands, string? from, string? to, bool defaultLatestYear = false)
    {
        var allOrds = page.Assets
            .SelectMany(a => a.Values)
            .Select(v => PeriodWindow.Ord(v.Year, v.Month))
            .ToList();
        int? availFromOrd = allOrds.Count > 0 ? allOrds.Min() : null;
        int? availToOrd = allOrds.Count > 0 ? allOrds.Max() : null;

        int latestYear = availToOrd.HasValue ? availToOrd.Value / 12 : 2025;
        int fallbackFrom = defaultLatestYear ? PeriodWindow.Ord(latestYear, 1) : availFromOrd ?? PeriodWindow.Ord(2025, 1);
        int fallbackTo = defaultLatestYear ? PeriodWindow.Ord(latestYear, 12) : availToOrd ?? PeriodWindow.Ord(2025, 12);
        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : fallbackFrom;
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : fallbackTo;
        if (toOrd < fromOrd) (fromOrd, toOrd) = (toOrd, fromOrd);

        bool InWindow(int year, int month)
        {
            var o = PeriodWindow.Ord(year, month);
            return o >= fromOrd && o <= toOrd;
        }

        static int StatusRank(string status) => status.ToLowerInvariant() switch
        {
            "completed" or "completions" or "views" => 0,
            "enrolled" => 1,
            _ => 2,
        };

        static bool IsCompletion(string status) => StatusRank(status) == 0;

        string BrandOf(EducationAsset a) => string.IsNullOrWhiteSpace(a.Brand) ? OtherBrand : a.Brand.Trim();

        var brandRank = clientBrands
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .Select((b, i) => (b.Name.ToLowerInvariant(), i))
            .ToDictionary(x => x.Item1, x => x.i);
        int BrandOrder(string brand) => brandRank.TryGetValue(brand.ToLowerInvariant(), out var i) ? i : brandRank.Count;
        string? BrandColour(string brand) =>
            clientBrands.FirstOrDefault(b => string.Equals(b.Name, brand, StringComparison.OrdinalIgnoreCase))?.Color;

        List<EducationPointDto> Points(IEnumerable<EducationAssetValue> values) => values
            .Where(v => IsCompletion(v.Status) && InWindow(v.Year, v.Month))
            .GroupBy(v => (v.Year, v.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new EducationPointDto(g.Key.Year, g.Key.Month, g.Sum(v => v.Value)))
            .ToList();

        var charts = page.Charts
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Title)
            .Select(c =>
            {
                var groups = new HashSet<string>(c.GroupLabels, StringComparer.OrdinalIgnoreCase);
                var chartAssets = page.Assets
                    .Where(a => groups.Count == 0 || groups.Contains(a.GroupLabel))
                    .OrderBy(a => a.SortOrder).ThenBy(a => a.Title)
                    .ToList();

                var brandSeries = chartAssets
                    .GroupBy(BrandOf)
                    .OrderBy(g => BrandOrder(g.Key)).ThenBy(g => g.Key)
                    .Select(g => new EducationSeriesDto(
                        g.Key,
                        g.Key,
                        BrandColour(g.Key),
                        Points(g.SelectMany(a => a.Values))))
                    .ToList();

                var activitySeries = chartAssets
                    .Select(a => new EducationSeriesDto(
                        a.Id.ToString(),
                        a.Title,
                        BrandColour(BrandOf(a)),
                        Points(a.Values)))
                    .ToList();

                return new EducationChartDto(
                    c.Id,
                    c.Title,
                    c.Subtitle,
                    c.SortOrder,
                    c.GroupLabels,
                    brandSeries,
                    activitySeries,
                    c.Annotations
                        .Where(a => InWindow(a.Year, a.Month))
                        .OrderBy(a => a.Year).ThenBy(a => a.Month)
                        .Select(a => new EducationAnnotationDto(a.Id, a.Brand, a.Year, a.Month, a.Text))
                        .ToList());
            })
            .ToList();

        var assets = page.Assets
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Title)
            .Select(a => new EducationAssetDto(
                a.Id,
                a.GroupLabel,
                a.Brand,
                a.Type,
                a.Title,
                a.Author,
                a.Expiry,
                a.SortOrder,
                a.Values
                    .GroupBy(v => v.Status)
                    .OrderBy(g => StatusRank(g.Key)).ThenBy(g => g.Key)
                    .Select(g =>
                    {
                        var points = g
                            .Where(v => InWindow(v.Year, v.Month))
                            .OrderBy(v => v.Year).ThenBy(v => v.Month)
                            .Select(v => new EducationPointDto(v.Year, v.Month, v.Value))
                            .ToList();
                        return new EducationAssetStatusDto(g.Key, points, points.Sum(p => p.Value));
                    })
                    .ToList()))
            .ToList();

        var period = new DashboardPeriodDto(
            From: PeriodWindow.ToYm(fromOrd),
            To: PeriodWindow.ToYm(toOrd),
            AvailableFrom: availFromOrd.HasValue ? PeriodWindow.ToYm(availFromOrd.Value) : null,
            AvailableTo: availToOrd.HasValue ? PeriodWindow.ToYm(availToOrd.Value) : null);

        var summary = new EducationPageSummaryDto(page.Id, page.Name, page.Slug, page.SortOrder, page.Charts.Count, page.Assets.Count);
        return new EducationPageResponse(summary, period, charts, assets);
    }
}
