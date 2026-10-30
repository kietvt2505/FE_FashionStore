-- New web orders use WEB-000001, WEB-000002, ... across all website instances.
-- Run after Supabase-Web-Orders.sql. Does not rename existing invoices or change money/stock/points.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
DO $$ BEGIN
    IF to_regclass('webstore.order_snapshot') IS NULL THEN
        RAISE EXCEPTION 'Apply Supabase-Web-Orders.sql first';
    END IF;
END $$;
LOCK TABLE public.hoadon,webstore.order_snapshot IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE webstore.order_snapshot ADD COLUMN IF NOT EXISTS request_id uuid;
CREATE UNIQUE INDEX IF NOT EXISTS ux_web_order_request
    ON webstore.order_snapshot(makh,request_id) WHERE request_id IS NOT NULL;
CREATE SEQUENCE IF NOT EXISTS webstore.web_order_number AS bigint MINVALUE 1 START WITH 1;
-- Only advance the sequence, including on re-runs. Never reuse codes from retained snapshots.
DO $$ DECLARE highest numeric; sequence_last bigint; sequence_called boolean; BEGIN
    SELECT COALESCE(max(substring(sohd FROM 5)::numeric),0) INTO highest FROM (
        SELECT sohd FROM public.hoadon UNION SELECT sohd FROM webstore.order_snapshot
    ) existing WHERE sohd ~ '^WEB-[0-9]{6,19}$';
    IF highest>=9223372036854775807 THEN RAISE EXCEPTION 'Web order sequence limit reached'; END IF;
    SELECT last_value,is_called INTO sequence_last,sequence_called FROM webstore.web_order_number;
    IF highest>sequence_last OR (highest=sequence_last AND NOT sequence_called) THEN
        PERFORM setval('webstore.web_order_number',highest::bigint,true);
    END IF;
END $$;
REVOKE ALL ON SEQUENCE webstore.web_order_number FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON SEQUENCE webstore.web_order_number FROM anon; END IF;
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON SEQUENCE webstore.web_order_number FROM authenticated; END IF;
END $$;
COMMIT;

-- Read-only verification; does not consume a number.
SELECT to_regclass('webstore.web_order_number') AS bo_dem_ma_don,
       EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='webstore'
           AND table_name='order_snapshot' AND column_name='request_id') AS co_khoa_chong_trung;
