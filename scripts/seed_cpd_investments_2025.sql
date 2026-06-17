-- One-off: replace the seeded "- CPD Packages" bucket placements with proper
-- per-article cpd_investment rows (from the Reckitt workbook's CPD-package notes).
-- Run AFTER the cpd_investment table exists. Idempotent: bucket delete is a no-op
-- once gone; inserts are guarded by NOT EXISTS.

SET search_path TO panwar_portals, public;

BEGIN;

-- 1. Remove the old bucket placements and their children (across all clients).
DELETE FROM placement_actual  WHERE "PlacementId" IN (SELECT "Id" FROM placement WHERE "Name" LIKE '%- CPD Packages');
DELETE FROM placement_kpi     WHERE "PlacementId" IN (SELECT "Id" FROM placement WHERE "Name" LIKE '%- CPD Packages');
DELETE FROM placement_comment WHERE "PlacementId" IN (SELECT "Id" FROM placement WHERE "Name" LIKE '%- CPD Packages');
UPDATE utm_link SET "PlacementId" = NULL WHERE "PlacementId" IN (SELECT "Id" FROM placement WHERE "Name" LIKE '%- CPD Packages');
DELETE FROM placement WHERE "Name" LIKE '%- CPD Packages';

-- 2. Seed the 6 CPD article lines for reckitt + veckitt (FY2025, audience Pharmacists).
--    All 6 are 'article' format (the only CPD format in the Reckitt 2025 workbook).
INSERT INTO cpd_investment ("Id", "BrandId", "AudienceId", "PublisherId", "Year", "Title", "Format", "Cost", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), b."Id", a."Id", p."Id", 2025, spec.title, 'article', spec.cost, now(), now()
FROM (VALUES
    ('Nurofen',              'Australian Journal of Pharmacy', 'Paracetamol Article',         24125),
    ('Nurofen',              'Australian Journal of Pharmacy', 'Migraine Article',            24125),
    ('Nurofen',              'Australian Pharmacist',          '10 Pains Article',            24100),
    ('Nurofen for Children', 'Australian Journal of Pharmacy', '8 Pains Article',             24125),
    ('Nurofen for Children', 'Australian Pharmacist',          'Immunisation Article',        25000),
    ('Gaviscon',             'Australian Journal of Pharmacy', 'Reflux/Constipation Article', 17800)
) AS spec(brand_name, pub_name, title, cost)
JOIN client c    ON c."Slug" IN ('reckitt', 'veckitt')
JOIN brand b     ON b."ClientId" = c."Id" AND b."Name" = spec.brand_name
JOIN audience a  ON a."ClientId" = c."Id" AND a."Name" = 'Pharmacists'
JOIN publisher p ON p."Name" = spec.pub_name
WHERE NOT EXISTS (
    SELECT 1 FROM cpd_investment x
    WHERE x."BrandId" = b."Id" AND x."AudienceId" = a."Id" AND x."PublisherId" = p."Id"
      AND x."Year" = 2025 AND x."Title" = spec.title
);

COMMIT;

-- Verify: SELECT b."Name", p."Name", x."Format", x."Title", x."Cost" FROM cpd_investment x
--   JOIN brand b ON b."Id"=x."BrandId" JOIN publisher p ON p."Id"=x."PublisherId"
--   JOIN client c ON c."Id"=b."ClientId" WHERE c."Slug"='reckitt' ORDER BY b."Name";
