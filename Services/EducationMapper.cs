using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

// Pass null bounds for the employee editor (unwindowed); client dash passes explicit from/to.
internal static class EducationMapper
{
    public static EducationPageResponse Build(
        EducationPage page, string? from, string? to, bool defaultLatestYear = false)
    {
        var allOrds = page.Charts
            .SelectMany(c => c.Series)
            .SelectMany(s => s.DataPoints)
            .Select(p => PeriodWindow.Ord(p.Year, p.Month))
            .Concat(page.Assets
                .SelectMany(a => a.Values)
                .Select(v => PeriodWindow.Ord(v.Year, v.Month)))
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

        var charts = page.Charts
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Title)
            .Select(c => new EducationChartDto(
                c.Id,
                c.Title,
                c.Subtitle,
                c.SortOrder,
                c.Series
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Label)
                    .Select(s => new EducationSeriesDto(
                        s.Id,
                        s.Label,
                        s.Color,
                        s.SortOrder,
                        s.DataPoints
                            .Where(p => InWindow(p.Year, p.Month))
                            .OrderBy(p => p.Year).ThenBy(p => p.Month)
                            .Select(p => new EducationPointDto(p.Year, p.Month, p.Value))
                            .ToList()))
                    .ToList(),
                c.Annotations
                    .Where(a => InWindow(a.Year, a.Month))
                    .OrderBy(a => a.Year).ThenBy(a => a.Month)
                    .Select(a => new EducationAnnotationDto(a.Id, a.EducationSeriesId, a.Year, a.Month, a.Text))
                    .ToList()))
            .ToList();

        // Completed-style statuses sort first per the workbook's reading order.
        static int StatusRank(string status) => status.ToLowerInvariant() switch
        {
            "completed" or "completions" or "views" => 0,
            "enrolled" => 1,
            _ => 2,
        };

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

        var summary = new EducationPageSummaryDto(page.Id, page.Name, page.Slug, page.SortOrder, page.Charts.Count);
        return new EducationPageResponse(summary, period, charts, assets);
    }
}
