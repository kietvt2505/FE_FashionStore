using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class WinFormsSyncInstaller
{
    public static async Task ApplyAsync(string connection,string sqlFile,string reportFile,bool localTest=false)
    {
        var settings=new NpgsqlConnectionStringBuilder(connection){Pooling=false,Timeout=20,CommandTimeout=120};
        if(localTest)
        {
            if(settings.Host is not ("localhost" or "127.0.0.1" or "::1")||!(settings.Database??"").StartsWith("fashionstore_native18_",StringComparison.Ordinal))throw new InvalidOperationException("Sai database thu nghiem.");
        }
        else if(settings.Database!="postgres"||!((settings.Host??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)||(settings.Username??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Sai project Supabase da thong nhat.");
        var script=await File.ReadAllTextAsync(sqlFile);
        if(Regex.IsMatch(script,@"\b(CREATE\s+TABLE|DROP\s+(TABLE|COLUMN))\b",RegexOptions.IgnoreCase))throw new InvalidOperationException("Khong duoc tao/xoa bang hoac xoa cot.");
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,c,tx);await cmd.ExecuteNonQueryAsync();}
        async Task<T> Scalar<T>(string text){await using var cmd=new NpgsqlCommand(text,c,tx);return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec("SET LOCAL lock_timeout='5s';SET LOCAL statement_timeout='120s'");
        var tables=await Scalar<string[]>("SELECT array_agg(tablename::text ORDER BY tablename) FROM pg_tables WHERE schemaname='public'");
        var expected=new[]{"baocaodoanhthu","bienbanxulyhangloi","chitietbaocaodoanhthu","chitietdoitra","chitiethoadon","chitietnhapkho","giaodichthanhtoanqr","hoadon","khachhang","khuyenmai","khuyenmaisanpham","loaisanpham","nhanvien","nhasanxuat","phanquyen","phieudoitra","phieunhapkho","sanpham"};
        if(!tables.Order().SequenceEqual(expected.Order()))throw new InvalidOperationException("Can dung 18 bang goc da thong nhat.");
        await Exec("LOCK TABLE "+string.Join(',',tables.Select(t=>"public.\""+t+"\""))+" IN SHARE ROW EXCLUSIVE MODE");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        var triggers=await Scalar<string>("""
            SELECT COALESCE(jsonb_agg(jsonb_build_object('name',t.tgname,'enabled',t.tgenabled,
                'definition',pg_get_triggerdef(t.oid),'function',pg_get_functiondef(t.tgfoid)) ORDER BY t.tgname),'[]'::jsonb)::text
            FROM pg_trigger t WHERE t.tgrelid='public.hoadon'::regclass AND NOT t.tgisinternal
            """);
        // Metadata only, never invoice/customer rows or connection credentials.
        await File.WriteAllTextAsync(Path.ChangeExtension(reportFile,"preflight.json"),triggers);
        async Task<Dictionary<string,string>> Fingerprints()
        {
            var result=new Dictionary<string,string>();
            foreach(var table in tables)
            {
                var remove=table switch{
                    "hoadon"=>"-ARRAY['loaihoadon','trangthaigiaohang','datrutonkho','daghinhanthanhtoan']::text[]",
                    "phieudoitra"=>"-'hinhthucxuly'","chitietdoitra"=>"-'loaigiaodich'",_=>""};
                result[table]=await Scalar<string>("SELECT md5(COALESCE(string_agg(v::text,',' ORDER BY v::text),'')) FROM(SELECT to_jsonb(t)"+remove+" AS v FROM public.\""+table+"\" t) q");
            }
            return result;
        }
        var before=await Fingerprints();
        await Exec(Regex.Replace(script,@"(?m)^\s*(BEGIN;|COMMIT;)\s*$",""));
        var after=await Fingerprints();
        if(!before.OrderBy(p=>p.Key).SequenceEqual(after.OrderBy(p=>p.Key)))throw new InvalidOperationException("Du lieu ngoai cac truong dong bo bi thay doi; hoan tac.");
        if(await Scalar<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'")!=18)throw new InvalidOperationException("So bang bi thay doi; hoan tac.");
        if(await Scalar<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='hoadon' AND column_name IN ('datrutonkho','daghinhanthanhtoan') AND data_type='boolean'")!=2)throw new InvalidOperationException("Cot giu kho khong dung kieu.");
        var pendingCodes=await Scalar<long>("""
            SELECT (SELECT count(*) FROM public.phieudoitra WHERE upper(btrim(nguondon)) IN ('WEB','WEBSITE')
                AND trangthai IN ('CHO_DUYET','CHO_TIEP_NHAN') AND hinhthucxuly IN ('TRA','DOI'))
              +(SELECT count(*) FROM public.chitietdoitra d JOIN public.phieudoitra p USING(sophieudt)
                WHERE upper(btrim(p.nguondon)) IN ('WEB','WEBSITE') AND p.trangthai IN ('CHO_DUYET','CHO_TIEP_NHAN') AND d.loaigiaodich IN ('TRA','DOI'))
            """);
        if(pendingCodes!=0)throw new InvalidOperationException("Con ma doi tra cu chua dong bo; hoan tac.");
        await tx.CommitAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        await File.WriteAllTextAsync(reportFile,JsonSerializer.Serialize(new{AppliedAt=DateTimeOffset.UtcNow,NativeTables=18,OtherDataUnchanged=true,PartnerCommit="6d11b10",
            SqlSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))),PendingReturnCodesRemaining=pendingCodes}));
    }
}
