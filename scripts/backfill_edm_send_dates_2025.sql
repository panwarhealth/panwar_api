-- One-off backfill: eDM first-send dates from the Reckitt workbook YTD Data notes.
-- Matches on brand + placement name (covers reckitt and the veckitt clone). Multi-send
-- eDMs get their earliest 2025 send. Only fills rows still missing a send date.
BEGIN;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-03-05'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AJP Solus eDM - Mini Caps w/ CTA to Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-05'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AJP Solus eDM - CTA to Paracetamol CPD Article + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-06-04'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AJP Solus eDM - CTA to Migraine CPD Article + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-09-01'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AJP Solus eDM - CPD Summary (BONUS)'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-10'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AP eDM SC - Acute Pain CPD'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-11'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AP Solus eDM -  CTA to Pain CPDs + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-24'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AP Solus eDM - Mini Caps w/ CTA to Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-17'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AP eDM Digital Banners (BONUS)'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-05-28'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'AP eDM SC - Tolerability CPD'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-25'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'Arterial eDM SC - CTA to Clinical Paper'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-05-31'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen' AND p."Name" = 'Healthed - Post-Webcast eDM (part of package)'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-05-07'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AJP Solus eDM - Immunisation Message'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-08-01'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AJP Solus eDM -  CTA to 8 Pains CPD Article + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-05'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AP eDM SC - CPD Podcast'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-04-21'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AP eDM Digital Banners'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-05-01'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AP Solus eDM - CTA to Immunisation CPD Article + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-04-14'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'AP eDM SC - Immunisation CPD'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-04-04'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'ADG Solus eDM - Immunisation Message + CTA to Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-03-28'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Nurofen for Children' AND p."Name" = 'Arterial eDM SC - CTA to Clinical Paper'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-03'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AJP Solus eDM - Gavi Gum/Senokot DA + CTA to Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-04-07'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AJP Solus eDM - CTA to Reflux/Constipation CPD + Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-01-30'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AP Solus eDM - Gavi Gum/Senokot DA + CTA to Portal'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-02-05'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AP eDM Digital Banners'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-01-20'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AP eDM SC - Lower GI Podcast'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-01-29'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AP eDM SC - ''Why is my PPI not working'''
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-11-13'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'AP Solus eDM - Resource Summary'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
UPDATE panwar_portals.placement p SET "StartDate" = DATE '2025-01-20'
FROM panwar_portals.brand b WHERE p."BrandId" = b."Id"
  AND b."Name" = 'Gaviscon' AND p."Name" = 'Arterial eDM SC - CTA to Module'
  AND p."TemplateId" IN (SELECT "Id" FROM panwar_portals.metric_template WHERE "Code" = 1)
  AND p."StartDate" IS NULL;
COMMIT;
