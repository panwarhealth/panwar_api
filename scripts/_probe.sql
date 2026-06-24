\pset pager off
SET search_path TO panwar_portals, public;
\echo === reckitt 2026 placements where StartDate month != the month(s) data is recorded in ===
SELECT p."Name", br."Name" AS brand, p."StartDate",
       EXTRACT(MONTH FROM p."StartDate")::int AS start_month,
       array_to_string(array_agg(DISTINCT a."Month" ORDER BY a."Month"), ',') AS actual_months
FROM placement p
JOIN brand br ON p."BrandId"=br."Id"
JOIN client c ON br."ClientId"=c."Id"
JOIN placement_actual a ON a."PlacementId"=p."Id" AND a."Year"=2026
WHERE c."Slug"='reckitt' AND p."StartDate" IS NOT NULL
GROUP BY p."Id", p."Name", br."Name", p."StartDate"
HAVING NOT (EXTRACT(MONTH FROM p."StartDate")::int = ANY(array_agg(DISTINCT a."Month")))
ORDER BY p."Name";
