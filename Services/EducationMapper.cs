using Panwar.Api.Models;
using Panwar.Api.Models.DTOs;

namespace Panwar.Api.Services;

/// <summary>
/// Builds the education page response tree from loaded entities. Shared by the
/// client read service (scoped to a month window) and the employee editor
/// (unwindowed, pass null bounds). A fully-loaded page (Charts → Series →
/// DataPoints, Charts → Annotations) is expected.
/// </summary>
internal static class EducationMapper
{
    public static EducationPageResponse Build(EducationPage page, string? from, string? to)
    {
        // Available span across every data point on the page.
        var allPoints = page.Charts
            .SelectMany(c => c.Series)
            .SelectMany(s => s.DataPoints)
            .ToList();
        int? availFromOrd = allPoints.Count > 0 ? allPoints.Min(p => PeriodWindow.Ord(p.Year, p.Month)) : null;
        int? availToOrd = allPoints.Count > 0 ? allPoints.Max(p => PeriodWindow.Ord(p.Year, p.Month)) : null;

        // Resolve the window. Null/unparseable bounds fall back to the full span
        // (or the fallback year when the page is empty).
        var fromOrd = PeriodWindow.TryParse(from, out var f) ? f : availFromOrd ?? PeriodWindow.Ord(2025, 1);
        var toOrd = PeriodWindow.TryParse(to, out var t) ? t : availToOrd ?? PeriodWindow.Ord(2025, 12);
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

        var period = new DashboardPeriodDto(
            From: PeriodWindow.ToYm(fromOrd),
            To: PeriodWindow.ToYm(toOrd),
            AvailableFrom: availFromOrd.HasValue ? PeriodWindow.ToYm(availFromOrd.Value) : null,
            AvailableTo: availToOrd.HasValue ? PeriodWindow.ToYm(availToOrd.Value) : null);

        var summary = new EducationPageSummaryDto(page.Id, page.Name, page.Slug, page.SortOrder, page.Charts.Count);
        return new EducationPageResponse(summary, period, charts);
    }
}
