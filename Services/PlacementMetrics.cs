using Panwar.Api.Models;
using Panwar.Api.Models.Enums;

namespace Panwar.Api.Services;

internal static class PlacementMetrics
{
    public static void EnsurePrintImpressions(IEnumerable<Placement> placements)
    {
        foreach (var p in placements.Where(p => p.Template.Code == MetricTemplateCode.Print))
        {
            foreach (var m in p.Actuals.GroupBy(a => (a.Year, a.Month)).ToList())
            {
                if (m.Any(a => a.MetricKey == "impressions")) continue;
                var circ = m.Where(a => a.MetricKey == "circulation").Sum(a => a.Value);
                var places = m.Where(a => a.MetricKey == "placements_count").Sum(a => a.Value);
                if (circ <= 0 || places <= 0) continue;
                p.Actuals.Add(new PlacementActual
                {
                    PlacementId = p.Id,
                    Year = m.Key.Year,
                    Month = m.Key.Month,
                    MetricKey = "impressions",
                    Value = circ * places,
                });
            }

            if (!p.Kpis.Any(k => k.MetricKey == "impressions"))
            {
                var circ = p.Kpis.Where(k => k.MetricKey == "circulation").Sum(k => k.TargetValue);
                var places = p.Kpis.Where(k => k.MetricKey == "placements_count").Sum(k => k.TargetValue);
                if (circ > 0 && places > 0)
                    p.Kpis.Add(new PlacementKpi { PlacementId = p.Id, MetricKey = "impressions", TargetValue = circ * places });
            }
        }
    }
}
