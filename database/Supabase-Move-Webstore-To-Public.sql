-- Existing installations only. Stop the old website backend during the cutover.
-- Atomic, data-preserving move; NEVER DROP SCHEMA CASCADE.
BEGIN;
SET LOCAL lock_timeout='10s';
SET LOCAL statement_timeout='120s';
SELECT pg_advisory_xact_lock(19579,20261102);
DO $migration$
DECLARE r record; target_namespace oid; source_namespace oid; definition text;
        before_count bigint; after_count bigint; original_hash text; moved_hash text;
        role_name text; signature text; command_kind text;
BEGIN
    target_namespace:=to_regnamespace('public');
    source_namespace:=to_regnamespace('webstore');
    IF target_namespace IS NULL OR to_regclass('public.sanpham') IS NULL
        OR to_regclass('public.khachhang') IS NULL OR to_regclass('public.hoadon') IS NULL THEN
        RAISE EXCEPTION 'Shared partner public tables are missing; wrong database or incomplete installation';
    END IF;
    IF source_namespace IS NULL THEN
        IF to_regclass('public.customer_account') IS NULL OR to_regclass('public.order_snapshot') IS NULL THEN
            RAISE EXCEPTION 'Neither the legacy nor migrated website installation was found';
        END IF;
        RAISE NOTICE 'Already migrated: webstore is absent';
        RETURN;
    END IF;
    -- Refuse all name collisions, including indexes and sequences moved with tables.
    IF EXISTS(SELECT 1 FROM pg_class s JOIN pg_class t ON t.relname=s.relname
        WHERE s.relnamespace=source_namespace AND t.relnamespace=target_namespace) THEN
        RAISE EXCEPTION 'Relation name collision between public and webstore; no objects moved';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_type s JOIN pg_type t ON t.typname=s.typname
        WHERE s.typnamespace=source_namespace AND t.typnamespace=target_namespace) THEN
        RAISE EXCEPTION 'Type name collision between public and webstore; no objects moved';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_proc s JOIN pg_proc t ON t.proname=s.proname AND t.proargtypes=s.proargtypes
        WHERE s.pronamespace=source_namespace AND t.pronamespace=target_namespace) THEN
        RAISE EXCEPTION 'Routine signature collision between public and webstore; no objects moved';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_depend d WHERE d.refclassid='pg_extension'::regclass AND d.deptype='e'
        AND ((d.classid='pg_class'::regclass AND d.objid IN(SELECT oid FROM pg_class WHERE relnamespace=source_namespace))
          OR (d.classid='pg_proc'::regclass AND d.objid IN(SELECT oid FROM pg_proc WHERE pronamespace=source_namespace)))) THEN
        RAISE EXCEPTION 'Extension-owned objects need a separate migration';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_class WHERE relnamespace=source_namespace AND relkind NOT IN('r','p','i','I','S','v','m','c'))
        OR EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace=source_namespace AND prokind NOT IN('f','p')) THEN
        RAISE EXCEPTION 'Unsupported schema object; review before migrating';
    END IF;
    IF to_regclass('public.web_schema_migration_20261102') IS NOT NULL THEN
        RAISE EXCEPTION 'Migration journal already exists while webstore is present; inspect previous rollback first';
    END IF;
    -- The journal is a metadata/definition backup, not a duplicate of customer rows.
    CREATE TABLE public.web_schema_migration_20261102(
        object_kind text NOT NULL, object_oid oid NOT NULL, object_name text NOT NULL,
        original_schema text NOT NULL, original_definition text, original_acl jsonb,
        row_count bigint, content_hash text, original_rls boolean, original_force_rls boolean,
        recorded_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
        PRIMARY KEY(object_kind,object_oid)
    );
    COMMENT ON TABLE public.web_schema_migration_20261102 IS 'Data-preserving webstore to public migration journal, 2026-11-02';
    ALTER TABLE public.web_schema_migration_20261102 ENABLE ROW LEVEL SECURITY;
    REVOKE ALL ON public.web_schema_migration_20261102 FROM PUBLIC;
    FOR role_name IN SELECT rolname FROM pg_roles WHERE rolname IN('anon','authenticated') LOOP
        EXECUTE format('REVOKE ALL ON public.web_schema_migration_20261102 FROM %I',role_name);
    END LOOP;
    INSERT INTO public.web_schema_migration_20261102(object_kind,object_oid,object_name,original_schema,original_definition)
    SELECT 'schema',oid,nspname,nspname,pg_get_userbyid(nspowner) FROM pg_namespace WHERE oid=source_namespace;
    INSERT INTO public.web_schema_migration_20261102(object_kind,object_oid,object_name,original_schema,original_acl,original_rls,original_force_rls)
    SELECT 'relation',c.oid,c.relname,'webstore',
        (SELECT jsonb_agg(to_jsonb(a)) FROM aclexplode(COALESCE(c.relacl,acldefault(CASE WHEN c.relkind='S' THEN 's'::"char" ELSE 'r'::"char" END,c.relowner))) a),
        c.relrowsecurity,c.relforcerowsecurity
    FROM pg_class c WHERE c.relnamespace=source_namespace AND c.relkind IN('r','p','S','v','m','c');
    INSERT INTO public.web_schema_migration_20261102(object_kind,object_oid,object_name,original_schema,original_definition,original_acl)
    SELECT 'routine',p.oid,p.proname,n.nspname,pg_get_functiondef(p.oid),
        (SELECT jsonb_agg(to_jsonb(a)) FROM aclexplode(COALESCE(p.proacl,acldefault('f',p.proowner))) a)
    FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
    WHERE p.prokind IN('f','p') AND (p.pronamespace=source_namespace OR
        (n.nspname='public' AND (p.prosrc ~* '\mwebstore\M' OR array_to_string(p.proconfig,',') ~* '\mwebstore\M')));
    INSERT INTO public.web_schema_migration_20261102(object_kind,object_oid,object_name,original_schema)
    SELECT 'type',t.oid,t.typname,'webstore' FROM pg_type t
    WHERE t.typnamespace=source_namespace AND t.typtype IN('e','d') AND t.typrelid=0;

    -- Block writes before taking counts/fingerprints; no rows are copied or deleted.
    FOR r IN SELECT c.oid,c.relname FROM pg_class c WHERE c.relnamespace=source_namespace AND c.relkind IN('r','p') ORDER BY c.relname LOOP
        EXECUTE format('LOCK TABLE webstore.%I IN ACCESS EXCLUSIVE MODE',r.relname);
        EXECUTE format('SELECT count(*),md5(COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,''[]'')) FROM webstore.%I t',r.relname)
            INTO before_count,original_hash;
        UPDATE public.web_schema_migration_20261102 SET row_count=before_count,content_hash=original_hash WHERE object_kind='relation' AND object_oid=r.oid;
    END LOOP;
    FOR r IN SELECT t.oid,t.typname,t.typtype FROM pg_type t JOIN public.web_schema_migration_20261102 j ON j.object_kind='type' AND j.object_oid=t.oid LOOP
        EXECUTE format('ALTER %s webstore.%I SET SCHEMA public',CASE WHEN r.typtype='d' THEN 'DOMAIN' ELSE 'TYPE' END,r.typname);
    END LOOP;
    FOR r IN SELECT c.oid,c.relname,c.relkind FROM pg_class c WHERE c.relnamespace=source_namespace AND c.relkind IN('r','p','c') ORDER BY c.relname LOOP
        EXECUTE format('ALTER %s webstore.%I SET SCHEMA public',CASE WHEN r.relkind='c' THEN 'TYPE' ELSE 'TABLE' END,r.relname);
    END LOOP;
    -- Owned identity/serial sequences and indexes moved with their table. Move remaining sequences/views.
    FOR r IN SELECT c.oid,c.relname,c.relkind FROM pg_class c WHERE c.relnamespace=source_namespace AND c.relkind IN('S','v','m') ORDER BY c.relname LOOP
        command_kind:=CASE r.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END;
        EXECUTE format('ALTER %s webstore.%I SET SCHEMA public',command_kind,r.relname);
    END LOOP;
    FOR r IN SELECT p.oid,p.proname,p.prokind,pg_get_function_identity_arguments(p.oid) args
        FROM pg_proc p WHERE p.pronamespace=source_namespace ORDER BY p.oid LOOP
        EXECUTE format('ALTER %s webstore.%I(%s) SET SCHEMA public',CASE WHEN r.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,r.proname,r.args);
    END LOOP;
    -- ALTER ... SET SCHEMA does not rewrite string-bodied SQL/PLpgSQL. Keep OIDs, ownership and trigger links.
    FOR r IN SELECT p.oid FROM pg_proc p JOIN public.web_schema_migration_20261102 j ON j.object_kind='routine' AND j.object_oid=p.oid ORDER BY p.oid LOOP
        definition:=regexp_replace(pg_get_functiondef(r.oid),'\mwebstore\M','public','gi');
        EXECUTE definition;
    END LOOP;
    -- public is API-visible: moving private objects must not give anon/authenticated new access.
    FOR r IN SELECT c.relname,c.relkind FROM pg_class c JOIN public.web_schema_migration_20261102 j ON j.object_kind='relation' AND j.object_oid=c.oid WHERE c.relkind<>'c' LOOP
        command_kind:=CASE WHEN r.relkind='S' THEN 'SEQUENCE' ELSE 'TABLE' END;
        EXECUTE format('REVOKE ALL ON %s public.%I FROM PUBLIC',command_kind,r.relname);
        FOR role_name IN SELECT rolname FROM pg_roles WHERE rolname IN('anon','authenticated') LOOP
            EXECUTE format('REVOKE ALL ON %s public.%I FROM %I',command_kind,r.relname,role_name);
        END LOOP;
    END LOOP;
    FOR r IN SELECT p.proname,p.prokind,pg_get_function_identity_arguments(p.oid) args FROM pg_proc p
        JOIN public.web_schema_migration_20261102 j ON j.object_kind='routine' AND j.object_oid=p.oid AND j.original_schema='webstore' LOOP
        signature:=format('%s public.%I(%s)',CASE WHEN r.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,r.proname,r.args);
        EXECUTE 'REVOKE ALL ON '||signature||' FROM PUBLIC';
        FOR role_name IN SELECT rolname FROM pg_roles WHERE rolname IN('anon','authenticated') LOOP
            EXECUTE 'REVOKE ALL ON '||signature||format(' FROM %I',role_name);
        END LOOP;
    END LOOP;
    -- Verify identical table OIDs, row contents and RLS flags before deleting the EMPTY schema.
    FOR r IN SELECT c.oid,c.relname,c.relrowsecurity,c.relforcerowsecurity,j.* FROM pg_class c
        JOIN public.web_schema_migration_20261102 j ON j.object_kind='relation' AND j.object_oid=c.oid WHERE j.row_count IS NOT NULL LOOP
        IF (SELECT relnamespace FROM pg_class WHERE oid=r.object_oid)<>target_namespace
            OR r.relrowsecurity IS DISTINCT FROM r.original_rls OR r.relforcerowsecurity IS DISTINCT FROM r.original_force_rls THEN
            RAISE EXCEPTION 'Identity or RLS verification failed for %',r.object_name;
        END IF;
        EXECUTE format('SELECT count(*),md5(COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,''[]'')) FROM public.%I t',r.object_name)
            INTO after_count,moved_hash;
        IF after_count<>r.row_count OR moved_hash IS DISTINCT FROM r.content_hash THEN
            RAISE EXCEPTION 'Data verification failed for %',r.object_name;
        END IF;
    END LOOP;
    IF EXISTS(SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public'
        AND (p.prosrc ~* '\mwebstore\M' OR array_to_string(p.proconfig,',') ~* '\mwebstore\M')) THEN
        RAISE EXCEPTION 'A public routine still references webstore';
    END IF;
    DROP SCHEMA webstore RESTRICT;
END $migration$;
NOTIFY pgrst,'reload schema';
COMMIT;
SELECT current_database() AS database_name,to_regnamespace('webstore') IS NULL AS old_schema_removed,
    to_regclass('public.customer_account') IS NOT NULL AS accounts_in_public,
    to_regclass('public.return_request') IS NOT NULL AS returns_in_public;
