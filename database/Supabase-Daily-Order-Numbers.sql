-- Apply after Supabase-Short-Order-Numbers.sql. Old invoice codes remain unchanged.
BEGIN;
SET LOCAL lock_timeout='5s';
CREATE TABLE IF NOT EXISTS webstore.daily_order_number (
    sohd varchar(30) PRIMARY KEY,
    order_day date NOT NULL,
    suffix integer NOT NULL CHECK(suffix BETWEEN 1 AND 9999),
    UNIQUE(order_day,suffix)
);
CREATE OR REPLACE FUNCTION webstore.allocate_order_number(p_day date DEFAULT ((CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date))
RETURNS varchar LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
DECLARE start_at integer; n integer; candidate varchar; BEGIN
    IF p_day IS NULL THEN RAISE EXCEPTION 'Order date required'; END IF;
    PERFORM pg_advisory_xact_lock(19579,p_day-DATE '2000-01-01');
    start_at=floor(random()*9999)::integer;
    FOR i IN 0..9998 LOOP
        n=(start_at+i)%9999+1;
        candidate='HD'||to_char(p_day,'DDMMYYYY')||'-'||lpad(n::text,4,'0');
        IF NOT EXISTS(SELECT 1 FROM public.hoadon WHERE sohd=candidate) AND
           NOT EXISTS(SELECT 1 FROM webstore.order_snapshot WHERE sohd=candidate) THEN
            INSERT INTO webstore.daily_order_number(sohd,order_day,suffix) VALUES(candidate,p_day,n)
            ON CONFLICT DO NOTHING;
            IF FOUND THEN RETURN candidate; END IF;
        END IF;
    END LOOP;
    RAISE EXCEPTION 'DAILY_ORDER_NUMBER_LIMIT';
END $$;
ALTER TABLE webstore.daily_order_number ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON webstore.daily_order_number FROM PUBLIC;
REVOKE ALL ON FUNCTION webstore.allocate_order_number(date) FROM PUBLIC;
DO $$ DECLARE r text; BEGIN
    FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
        IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname=r) THEN
            EXECUTE format('REVOKE ALL ON webstore.daily_order_number FROM %I',r);
            EXECUTE format('REVOKE ALL ON FUNCTION webstore.allocate_order_number(date) FROM %I',r);
        END IF;
    END LOOP;
END $$;
COMMIT;
SELECT to_regprocedure('webstore.allocate_order_number(date)') IS NOT NULL AS co_ma_hd_theo_ngay;
