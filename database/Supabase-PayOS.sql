-- Apply AFTER Supabase-Online-Payments.sql. No existing invoice is paid by this migration.
BEGIN;
SET LOCAL lock_timeout='5s';
ALTER TABLE public.yeucauthanhtoantructuyen DROP CONSTRAINT IF EXISTS yeucauthanhtoantructuyen_provider_check;
ALTER TABLE public.yeucauthanhtoantructuyen ADD CONSTRAINT yeucauthanhtoantructuyen_provider_check CHECK(provider IN ('VNPAY','ZALOPAY','PAYOS'));
ALTER TABLE public.yeucauthanhtoantructuyen DROP CONSTRAINT IF EXISTS yeucauthanhtoantructuyen_method_check;
ALTER TABLE public.yeucauthanhtoantructuyen ADD CONSTRAINT yeucauthanhtoantructuyen_method_check CHECK(method IN ('VNPAY','VISA','ZALOPAY','PAYOS'));
ALTER TABLE public.yeucauthanhtoantructuyen ADD COLUMN IF NOT EXISTS provider_link_id varchar(64);
CREATE SEQUENCE IF NOT EXISTS public.payos_payment_number AS bigint MINVALUE 1 MAXVALUE 9007199254740991;
DO $$ DECLARE initial bigint; previous bigint; BEGIN
    initial=floor(extract(epoch FROM clock_timestamp())*1000)::bigint;
    SELECT last_value INTO previous FROM public.payos_payment_number;
    IF previous<initial THEN PERFORM setval('public.payos_payment_number',initial,true); END IF;
END $$;
REVOKE ALL ON SEQUENCE public.payos_payment_number FROM PUBLIC;
DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='anon') THEN REVOKE ALL ON SEQUENCE public.payos_payment_number FROM anon; END IF;
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='authenticated') THEN REVOKE ALL ON SEQUENCE public.payos_payment_number FROM authenticated; END IF;
END $$;
COMMIT;
SELECT to_regclass('public.payos_payment_number') IS NOT NULL AS co_payos;
