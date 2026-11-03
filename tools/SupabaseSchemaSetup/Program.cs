using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class SchemaInstaller
{
    private static string Q(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    public static async Task ApplyAsync(string connectionString,string sqlFile,string contractFile,string outputFile,bool localTest=false)
    {
        if(!localTest)throw new InvalidOperationException("Bo cai 17 bang bo sung da ngung su dung. Du an chi dung 18 bang goc va cac cot theo so do da chot.");
        var settings=new NpgsqlConnectionStringBuilder(connectionString){Timeout=20,CommandTimeout=120,Pooling=false};
        var loopback=settings.Host is "localhost" or "127.0.0.1" or "::1";
        if(localTest)
        {
            if(!loopback||!(settings.Database??"").StartsWith("fashionstore_vietnamese_",StringComparison.Ordinal))throw new InvalidOperationException("Sai database thu nghiem.");
        }
        using var contract=JsonDocument.Parse(await File.ReadAllTextAsync(contractFile));
        var native=contract.RootElement.GetProperty("relations").EnumerateArray().Where(r=>r.GetProperty("kind").GetString()=="r").ToArray();
        var text=await File.ReadAllTextAsync(sqlFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        var addedTables=Regex.Matches(text,@"CREATE TABLE IF NOT EXISTS public\.([a-z]+)\b").Select(m=>m.Groups[1].Value).Distinct().Order().ToArray();
        if(native.Length!=18||addedTables.Length!=17)throw new InvalidOperationException("Ban cai hoac ban cau truc khong dung phien ban da kiem thu.");
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();
        await using var tx=await c.BeginTransactionAsync();
        async Task Exec(string sql){await using var cmd=new NpgsqlCommand(sql,c,tx){CommandTimeout=120};await cmd.ExecuteNonQueryAsync();}
        async Task<T> Value<T>(string sql){await using var cmd=new NpgsqlCommand(sql,c,tx){CommandTimeout=120};return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec("SET LOCAL lock_timeout='5s';SET LOCAL statement_timeout='120s'");
        await Exec("LOCK TABLE "+string.Join(',',native.Select(t=>"public."+Q(t.GetProperty("name").GetString()!)).Order())+" IN SHARE ROW EXCLUSIVE MODE");
        foreach(var table in native)
        {
            var name=table.GetProperty("name").GetString()!;
            var expected=table.GetProperty("columns").EnumerateArray().Select(a=>a.GetProperty("name").GetString()+":"+a.GetProperty("type").GetString()).ToArray();
            await using var columns=new NpgsqlCommand("SELECT a.attname||':'||format_type(a.atttypid,a.atttypmod) FROM pg_attribute a WHERE a.attrelid=to_regclass(@table) AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum",c,tx);
            columns.Parameters.AddWithValue("table","public."+name);var actual=new List<string>();
            await using(var reader=await columns.ExecuteReaderAsync())while(await reader.ReadAsync())actual.Add(reader.GetString(0));
            if(!expected.SequenceEqual(actual))throw new InvalidOperationException("Cau truc bang "+name+" da thay doi. Can kiem tra lai truoc khi cai.");
        }
        async Task<Dictionary<string,string>> Fingerprints()
        {
            var result=new Dictionary<string,string>();
            foreach(var table in native){var name=table.GetProperty("name").GetString()!;result[name]=await Value<string>("SELECT md5(COALESCE(string_agg(to_jsonb(t)::text,',' ORDER BY to_jsonb(t)::text),'')) FROM public."+Q(name)+" t");}
            return result;
        }
        var before=await Fingerprints();
        // The caller owns the transaction, so verification happens before the only commit.
        text=Regex.Replace(text,@"(?m)^\s*(BEGIN;|COMMIT;)\s*$","");
        await Exec(text);
        var after=await Fingerprints();
        if(!before.OrderBy(p=>p.Key).SequenceEqual(after.OrderBy(p=>p.Key)))throw new InvalidOperationException("Du lieu bang goc thay doi; toan bo giao dich se duoc hoan tac.");
        foreach(var table in addedTables)
            if(!await Value<bool>("SELECT to_regclass('public."+table+"') IS NOT NULL"))throw new InvalidOperationException("Thieu bang "+table);
        var tableCount=await Value<long>("SELECT count(*) FROM pg_tables WHERE schemaname='public'");
        var webOrders=await Value<long>("SELECT count(*) FROM public.thongtindonhang");
        await tx.CommitAsync();
        var result=new{AppliedAt=DateTimeOffset.UtcNow,SqlSha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sqlFile))),
            NativeTablesPreserved=native.Length,NativeDataUnchanged=true,WebsiteTables=addedTables,PublicTableCount=tableCount,WebOrdersLinked=webOrders};
        await File.WriteAllTextAsync(outputFile,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}

public static class SchemaSetupProgram
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length is not (3 or 4) && args.FirstOrDefault() is not ("--audit-logins" or "--restore-login"))throw new InvalidOperationException("Can duong dan SQL, metadata va ket qua.");
            var connection=Environment.GetEnvironmentVariable("FASHIONSTORE_SCHEMA_SETUP_CONNECTION");
            if(string.IsNullOrWhiteSpace(connection))throw new InvalidOperationException("Chua nhap chuoi ket noi Supabase.");
            if(args.Length==6 && args[0]=="--restore-login")
            {
                var password=Environment.GetEnvironmentVariable("FASHIONSTORE_LOGIN_PASSWORD");
                Environment.SetEnvironmentVariable("FASHIONSTORE_LOGIN_PASSWORD",null);
                if(string.IsNullOrWhiteSpace(password))throw new InvalidOperationException("Chua nhap mat khau moi.");
                await CustomerLoginRestore.RestoreAsync(connection,args[2],args[3],args[4],args[5],password,args[1]);return 0;
            }
            if(args.Length>=3 && args[0]=="--audit-logins")
            {
                await LoginAccountAudit.ReadAsync(connection,args[1],args.Skip(2).ToArray());return 0;
            }
            if(args.Length==4 && args[0]=="--columns")
            {
                await ColumnSchemaInstaller.ApplyAsync(connection,args[1],args[2],args[3]);
                Console.WriteLine("DA BO SUNG COT TIENG VIET. Khong tao/xoa bang; du lieu cot goc giu nguyen.");return 0;
            }
            if(args.Length==3 && args[0]=="--winforms")
            {
                await WinFormsSyncInstaller.ApplyAsync(connection,args[1],args[2]);
                Console.WriteLine("DA DONG BO WINFORMS. Giu nguyen 18 bang, khong thay doi tai khoan/mat khau/tien/ton kho.");return 0;
            }
            if(args.Length!=3)throw new InvalidOperationException("Tham so khong hop le.");
            await SchemaInstaller.ApplyAsync(connection,args[0],args[1],args[2]);
            Console.WriteLine("DA CAI BANG TIENG VIET. Du lieu 18 bang goc duoc giu nguyen.");return 0;
        }
        catch(Exception error)
        {
            Console.WriteLine(error is PostgresException pg?"CHUA AP DUNG: "+pg.SqlState+" "+pg.MessageText:
                error is NpgsqlException?"CHUA AP DUNG: Khong ket noi duoc Supabase hoac giao dich khong hoan tat.":"CHUA AP DUNG: "+error.Message);
            return 1;
        }
    }
}
