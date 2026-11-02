-- Run in the existing Supabase project's SQL Editor.
-- Inspection only: no application data, schema, permissions or objects are changed.
BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '30s';

-- 1. A database contains schemas. These results distinguish those two levels.
SELECT current_database() AS database_name, current_user AS connected_role,
       current_schema() AS default_schema, current_schemas(false) AS search_path;
SELECT datname AS database_name FROM pg_database
WHERE NOT datistemplate AND datallowconn ORDER BY datname;
SELECT schema_name FROM information_schema.schemata
WHERE schema_name NOT LIKE 'pg_%' AND schema_name <> 'information_schema'
ORDER BY schema_name;

-- 2. Objects in the partner and website schemas (row counts are estimates only).
SELECT n.nspname AS schema_name, c.relname AS object_name,
       CASE c.relkind WHEN 'r' THEN 'table' WHEN 'p' THEN 'partitioned table'
            WHEN 'v' THEN 'view' WHEN 'm' THEN 'materialized view'
            WHEN 'S' THEN 'sequence' END AS object_type,
       CASE WHEN c.relkind IN ('r','p') THEN c.reltuples::bigint END AS estimated_rows,
       c.relrowsecurity AS rls_enabled, c.relforcerowsecurity AS rls_forced
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname IN ('public','webstore') AND c.relkind IN ('r','p','v','m','S')
ORDER BY n.nspname,c.relkind,c.relname;

-- 3. Same-name objects must be reviewed before any SET SCHEMA operation.
SELECT w.relname AS conflicting_name, w.relkind AS webstore_kind, p.relkind AS public_kind
FROM pg_class w JOIN pg_namespace wn ON wn.oid=w.relnamespace AND wn.nspname='webstore'
JOIN pg_class p ON p.relname=w.relname
JOIN pg_namespace pn ON pn.oid=p.relnamespace AND pn.nspname='public'
WHERE w.relkind IN ('r','p','v','m','S') AND p.relkind IN ('r','p','v','m','S')
ORDER BY w.relname;

-- 4. Compare the actual shared business tables with the website additions.
SELECT table_schema,table_name,ordinal_position,column_name,data_type,
       is_nullable,column_default,is_identity
FROM information_schema.columns
WHERE table_schema IN ('public','webstore') AND table_name IN (
    'sanpham','loaisanpham','khachhang','hoadon','chitiethoadon',
    'phieudoitra','chitietdoitra','return_request','customer_account',
    'cart','cart_line','order_snapshot','order_line_snapshot',
    'order_cancellation','product_review','order_refund')
ORDER BY table_schema,table_name,ordinal_position;

-- 5. Cross-schema foreign keys show relationships within this database.
SELECT ns.nspname AS source_schema,src.relname AS source_table,con.conname,
       nt.nspname AS target_schema,dst.relname AS target_table,
       pg_get_constraintdef(con.oid,true) AS definition
FROM pg_constraint con JOIN pg_class src ON src.oid=con.conrelid
JOIN pg_namespace ns ON ns.oid=src.relnamespace
JOIN pg_class dst ON dst.oid=con.confrelid
JOIN pg_namespace nt ON nt.oid=dst.relnamespace
WHERE con.contype='f' AND (ns.nspname='webstore' OR nt.nspname='webstore'
    OR (ns.nspname='public' AND src.relname IN ('hoadon','chitiethoadon','phieudoitra','chitietdoitra')))
ORDER BY ns.nspname,src.relname,con.conname;

-- 6. Stored SQL and triggers also depend on explicit schema names.
SELECT n.nspname AS routine_schema,p.proname AS routine_name,
       pg_get_function_identity_arguments(p.oid) AS identity_arguments,
       p.prosecdef AS security_definer,p.proconfig AS function_settings,
       strpos(p.prosrc,'webstore.')>0 AS body_mentions_webstore,
       strpos(p.prosrc,'public.')>0 AS body_mentions_public
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE n.nspname IN ('public','webstore') AND p.prokind IN ('f','p')
ORDER BY n.nspname,p.proname,identity_arguments;
SELECT ns.nspname AS table_schema,c.relname AS table_name,t.tgname AS trigger_name,
       np.nspname AS function_schema,p.proname AS function_name,
       pg_get_triggerdef(t.oid,true) AS definition
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace ns ON ns.oid=c.relnamespace
JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_namespace np ON np.oid=p.pronamespace
WHERE NOT t.tgisinternal AND (ns.nspname IN ('public','webstore') OR np.nspname='webstore')
ORDER BY ns.nspname,c.relname,t.tgname;

-- 7. Schema location is separate from access permissions. Preserve these during any migration.
SELECT schemaname,tablename,policyname,roles,cmd,qual,with_check
FROM pg_policies WHERE schemaname IN ('public','webstore')
ORDER BY schemaname,tablename,policyname;
SELECT table_schema,table_name,grantee,privilege_type
FROM information_schema.table_privileges
WHERE table_schema IN ('public','webstore') AND grantee IN ('PUBLIC','anon','authenticated','service_role')
ORDER BY table_schema,table_name,grantee,privilege_type;

ROLLBACK;
