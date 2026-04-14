-- One-shot: deep-clone an existing client's data under a new name.
-- Edit the three variables below, then run once.
--
-- Copies: client row, brands, audiences, placements, placement_kpi, placement_actual.
-- Does NOT copy: publishers, metric templates, users (shared / separate concern).
--
-- Publishers/templates are shared across clients so we reuse them — no need to duplicate.

DO $$
DECLARE
    new_client_name text  := 'Veckitt Test';
    new_client_slug text  := 'veckitt';
    source_slug text      := 'reckitt';

    new_client_id uuid    := gen_random_uuid();
    source_client_id uuid;
BEGIN
    SELECT "Id" INTO source_client_id FROM panwar_portals.client WHERE "Slug" = source_slug;
    IF source_client_id IS NULL THEN
        RAISE EXCEPTION 'Source client with slug % not found', source_slug;
    END IF;

    -- 1. Client row (blue palette so it visually differs from Reckitt)
    INSERT INTO panwar_portals.client
        ("Id", "Name", "Slug", "LogoUrl", "PrimaryColor", "AccentColor", "CreatedAt", "UpdatedAt")
    VALUES
        (new_client_id, new_client_name, new_client_slug, NULL, '#2563EB', '#60A5FA', now(), now());

    -- 2. Brands (ID map preserves relationships)
    CREATE TEMP TABLE brand_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    INSERT INTO brand_map
    SELECT "Id", gen_random_uuid() FROM panwar_portals.brand WHERE "ClientId" = source_client_id;

    INSERT INTO panwar_portals.brand ("Id", "ClientId", "Name", "Slug", "CreatedAt", "UpdatedAt")
    SELECT bm.new_id, new_client_id, b."Name", b."Slug", now(), now()
    FROM panwar_portals.brand b
    JOIN brand_map bm ON b."Id" = bm.old_id;

    -- 3. Audiences
    CREATE TEMP TABLE audience_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    INSERT INTO audience_map
    SELECT "Id", gen_random_uuid() FROM panwar_portals.audience WHERE "ClientId" = source_client_id;

    INSERT INTO panwar_portals.audience ("Id", "ClientId", "Name", "Slug", "CreatedAt", "UpdatedAt")
    SELECT am.new_id, new_client_id, a."Name", a."Slug", now(), now()
    FROM panwar_portals.audience a
    JOIN audience_map am ON a."Id" = am.old_id;

    -- 4. Placements — remap brand + audience FKs
    CREATE TEMP TABLE placement_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL) ON COMMIT DROP;
    INSERT INTO placement_map
    SELECT p."Id", gen_random_uuid()
    FROM panwar_portals.placement p
    JOIN brand_map bm ON p."BrandId" = bm.old_id;

    INSERT INTO panwar_portals.placement (
        "Id", "BrandId", "AudienceId", "PublisherId", "TemplateId", "Name",
        "Objective", "AssetType", "CreativeCode", "OsCode", "UtmUrl", "ArtworkUrl",
        "MediaCost", "CpdInvestmentCost", "Circulation", "LiveMonths",
        "IsBonus", "IsCpdPackage", "TargetCourseId",
        "CreatedAt", "UpdatedAt"
    )
    SELECT
        pm.new_id, bm.new_id, am.new_id, p."PublisherId", p."TemplateId", p."Name",
        p."Objective", p."AssetType", p."CreativeCode", p."OsCode", p."UtmUrl", p."ArtworkUrl",
        p."MediaCost", p."CpdInvestmentCost", p."Circulation", p."LiveMonths",
        p."IsBonus", p."IsCpdPackage", p."TargetCourseId",
        now(), now()
    FROM panwar_portals.placement p
    JOIN placement_map pm ON p."Id" = pm.old_id
    JOIN brand_map bm ON p."BrandId" = bm.old_id
    JOIN audience_map am ON p."AudienceId" = am.old_id;

    -- 5. Placement KPIs
    INSERT INTO panwar_portals.placement_kpi ("Id", "PlacementId", "MetricKey", "TargetValue")
    SELECT gen_random_uuid(), pm.new_id, k."MetricKey", k."TargetValue"
    FROM panwar_portals.placement_kpi k
    JOIN placement_map pm ON k."PlacementId" = pm.old_id;

    -- 6. Placement actuals
    INSERT INTO panwar_portals.placement_actual ("Id", "PlacementId", "Year", "Month", "MetricKey", "Value", "Note")
    SELECT gen_random_uuid(), pm.new_id, a."Year", a."Month", a."MetricKey", a."Value", a."Note"
    FROM panwar_portals.placement_actual a
    JOIN placement_map pm ON a."PlacementId" = pm.old_id;

    RAISE NOTICE 'Cloned client % (id %) → % (id %)',
        source_slug, source_client_id, new_client_slug, new_client_id;
END $$;
