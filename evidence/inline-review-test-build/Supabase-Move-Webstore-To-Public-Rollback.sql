-- Emergency rollback paired with the migration journal. Stop the public-version backend first.
-- Moves the SAME objects back; does not discard rows written since the cutover.
BEGIN;
SET LOCAL lock_timeout='10s';
SET LOCAL statement_timeout='120s';
SELECT pg_advisory_xact_lock(19579,20261102);
DO $rollback$
DECLARE r record; a record; q record; target text; command_kind text; role_sql text;
BEGIN
    IF to_regclass('public.web_schema_migration_20261102') IS NULL THEN
        RAISE EXCEPTION 'Migration journal not found';
    END IF;
    IF to_regnamespace('webstore') IS NOT NULL THEN
        RAISE EXCEPTION 'webstore already exists; do not merge or overwrite it';
    END IF;
    IF EXISTS(SELECT 1 FROM public.web_schema_migration_20261102 j LEFT JOIN pg_class c ON c.oid=j.object_oid
        WHERE j.object_kind='relation' AND (c.oid IS NULL OR c.relnamespace<>'public'::regnamespace)) THEN
        RAISE EXCEPTION 'A migrated object was removed or moved elsewhere; manual review required';
    END IF;
    CREATE SCHEMA webstore;
    REVOKE ALL ON SCHEMA webstore FROM PUBLIC;
    FOR r IN SELECT t.oid,t.typname,t.typtype FROM pg_type t JOIN public.web_schema_migration_20261102 j ON j.object_kind='type' AND j.object_oid=t.oid LOOP
        EXECUTE format('ALTER %s public.%I SET SCHEMA webstore',CASE WHEN r.typtype='d' THEN 'DOMAIN' ELSE 'TYPE' END,r.typname);
    END LOOP;
    FOR r IN SELECT c.oid,c.relname,c.relkind FROM pg_class c JOIN public.web_schema_migration_20261102 j ON j.object_kind='relation' AND j.object_oid=c.oid WHERE c.relkind IN('r','p','c') ORDER BY c.relname LOOP
        EXECUTE format('ALTER %s public.%I SET SCHEMA webstore',CASE WHEN r.relkind='c' THEN 'TYPE' ELSE 'TABLE' END,r.relname);
    END LOOP;
    FOR r IN SELECT c.oid,c.relname,c.relkind FROM pg_class c JOIN public.web_schema_migration_20261102 j ON j.object_kind='relation' AND j.object_oid=c.oid
        WHERE c.relkind IN('S','v','m') AND c.relnamespace='public'::regnamespace ORDER BY c.relname LOOP
        EXECUTE format('ALTER %s public.%I SET SCHEMA webstore',CASE r.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END,r.relname);
    END LOOP;
    FOR r IN SELECT p.oid,p.proname,p.prokind,pg_get_function_identity_arguments(p.oid) args FROM pg_proc p
        JOIN public.web_schema_migration_20261102 j ON j.object_kind='routine' AND j.object_oid=p.oid AND j.original_schema='webstore' LOOP
        EXECUTE format('ALTER %s public.%I(%s) SET SCHEMA webstore',CASE WHEN r.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,r.proname,r.args);
    END LOOP;
    FOR r IN SELECT original_definition FROM public.web_schema_migration_20261102 WHERE object_kind='routine' ORDER BY object_oid LOOP
        EXECUTE r.original_definition;
    END LOOP;
    -- Restore the original effective table/sequence/routine grants, including grant options.
    FOR r IN SELECT j.*,c.relkind FROM public.web_schema_migration_20261102 j JOIN pg_class c ON j.object_kind='relation' AND j.object_oid=c.oid WHERE c.relkind<>'c' LOOP
        target:=format('%s webstore.%I',CASE WHEN r.relkind='S' THEN 'SEQUENCE' ELSE 'TABLE' END,r.object_name);
        FOR q IN SELECT DISTINCT x.grantee FROM pg_class c,LATERAL aclexplode(COALESCE(c.relacl,acldefault(CASE WHEN c.relkind='S' THEN 's'::"char" ELSE 'r'::"char" END,c.relowner))) x WHERE c.oid=r.object_oid LOOP
            role_sql:=CASE WHEN q.grantee=0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(q.grantee)) END;
            EXECUTE 'REVOKE ALL ON '||target||' FROM '||role_sql;
        END LOOP;
        FOR a IN SELECT * FROM jsonb_to_recordset(r.original_acl) AS x(grantee oid,privilege_type text,is_grantable boolean) LOOP
            role_sql:=CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(a.grantee)) END;
            EXECUTE 'GRANT '||a.privilege_type||' ON '||target||' TO '||role_sql||CASE WHEN a.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END;
        END LOOP;
    END LOOP;
    FOR r IN SELECT j.*,p.proname,p.prokind,n.nspname,pg_get_function_identity_arguments(p.oid) args FROM public.web_schema_migration_20261102 j
        JOIN pg_proc p ON j.object_kind='routine' AND j.object_oid=p.oid JOIN pg_namespace n ON n.oid=p.pronamespace LOOP
        target:=format('%s %I.%I(%s)',CASE WHEN r.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,r.nspname,r.proname,r.args);
        FOR q IN SELECT DISTINCT x.grantee FROM pg_proc p,LATERAL aclexplode(COALESCE(p.proacl,acldefault('f',p.proowner))) x WHERE p.oid=r.object_oid LOOP
            role_sql:=CASE WHEN q.grantee=0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(q.grantee)) END;
            EXECUTE 'REVOKE ALL ON '||target||' FROM '||role_sql;
        END LOOP;
        FOR a IN SELECT * FROM jsonb_to_recordset(r.original_acl) AS x(grantee oid,privilege_type text,is_grantable boolean) LOOP
            role_sql:=CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE quote_ident(pg_get_userbyid(a.grantee)) END;
            EXECUTE 'GRANT '||a.privilege_type||' ON '||target||' TO '||role_sql||CASE WHEN a.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END;
        END LOOP;
    END LOOP;
    SELECT original_definition INTO role_sql FROM public.web_schema_migration_20261102 WHERE object_kind='schema';
    EXECUTE format('ALTER SCHEMA webstore OWNER TO %I',role_sql);
END $rollback$;
NOTIFY pgrst,'reload schema';
COMMIT;
