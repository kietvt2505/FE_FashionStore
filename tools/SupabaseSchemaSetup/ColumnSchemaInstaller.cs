using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class ColumnSchemaInstaller
{
    public static readonly IReadOnlyDictionary<string,Dictionary<string,string>> Added = new Dictionary<string,Dictionary<string,string>>
    {
        ["khachhang"]=new(){["makhoiphucbam"]="character varying(64)",["hethanmakhoiphuc"]="timestamp with time zone",["phienmatkhaukhoiphuc"]="character varying(64)"},
        ["chitiethoadon"]=new(){["masplucmua"]="character varying(20)",["tensanphamlucmua"]="character varying(200)",["kichcolucmua"]="character varying(30)",["mausaclucmua"]="character varying(60)",["hinhanhlucmua"]="text",["sosao"]="smallint",["noidungdanhgia"]="character varying(2000)",["ngaydanhgia"]="timestamp with time zone",["trangthaidanhgia"]="character varying(20)"},
        ["hoadon"]=new(){["lydohuy"]="character varying(500)",["ngayhuy"]="timestamp with time zone"},
        ["phieudoitra"]=new(){["tennganhanghoantien"]="character varying(100)",["sotaikhoanhoantien"]="character varying(34)",["chutaikhoanhoantien"]="character varying(150)",["mathamchieuhoantien"]="character varying(100)",["ngayhoantien"]="timestamp with time zone"}
    };
    private static string Q(string name)=>"\""+name.Replace("\"","\"\"")+"\"";
    public static async Task ApplyAsync(string connectionString,string sqlFile,string contractFile,string reportFile,bool localTest=false)
    {
        var settings=new NpgsqlConnectionStringBuilder(connectionString){Pooling=false,Timeout=20,CommandTimeout=120,Timezone="UTC"};
        if(localTest)
        {
            if(settings.Host is not ("127.0.0.1" or "localhost" or "::1")||!(settings.Database??"").StartsWith("fashionstore_native18_",StringComparison.Ordinal))
                throw new InvalidOperationException("Sai database thu nghiem 18 bang.");
        }
        else if(settings.Database!="postgres"||!((settings.Host??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)||(settings.Username??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Chi ap dung tren project Khoa Luan Cu Nhan da thong nhat.");
        var sql=await File.ReadAllTextAsync(sqlFile);
        if(Regex.IsMatch(sql,@"\b(CREATE\s+TABLE|DROP\s+TABLE|DROP\s+COLUMN)\b",RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Bo cap nhat cot khong duoc tao/xoa bang hay xoa cot.");
        using var contract=JsonDocument.Parse(await File.ReadAllTextAsync(contractFile));
        var native=contract.RootElement.GetProperty("relations").EnumerateArray().Where(t=>t.GetProperty("kind").GetString()=="r").ToArray();
        if(native.Length!=18)throw new InvalidOperationException("Can ban doi chieu 18 bang goc.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,c,tx){CommandTimeout=120};await cmd.ExecuteNonQueryAsync();}
        async Task<T> Value<T>(string text){await using var cmd=new NpgsqlCommand(text,c,tx){CommandTimeout=120};return (T)(await cmd.ExecuteScalarAsync())!;}
        await Exec("SET LOCAL lock_timeout='5s';SET LOCAL statement_timeout='120s'");
        await Exec("LOCK TABLE "+string.Join(',',native.Select(t=>"public."+Q(t.GetProperty("name").GetString()!)).Order())+" IN SHARE ROW EXCLUSIVE MODE");
        var beforeTables=await Value<string[]>("SELECT array_agg(tablename::text ORDER BY tablename) FROM pg_tables WHERE schemaname='public'");
        async Task VerifyColumns(bool requireAdded)
        {
            foreach(var t in native)
            {
                var table=t.GetProperty("name").GetString()!;var columns=new Dictionary<string,(string Type,bool Required)>();
                await using var cmd=new NpgsqlCommand("SELECT attname,format_type(atttypid,atttypmod),attnotnull FROM pg_attribute WHERE attrelid=to_regclass(@table) AND attnum>0 AND NOT attisdropped",c,tx);cmd.Parameters.AddWithValue("table","public."+table);
                await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())columns[r.GetString(0)]=(r.GetString(1),r.GetBoolean(2));
                foreach(var expected in t.GetProperty("columns").EnumerateArray())
                    if(!columns.TryGetValue(expected.GetProperty("name").GetString()!,out var actual)||actual.Type!=expected.GetProperty("type").GetString()||actual.Required!=expected.GetProperty("not_null").GetBoolean())
                        throw new InvalidOperationException("Cot goc cua bang "+table+" da thay doi. Can doi chieu lai.");
                if(Added.TryGetValue(table,out var additions))foreach(var field in additions)
                    if(columns.TryGetValue(field.Key,out var actual)){if(actual.Type!=field.Value||actual.Required)throw new InvalidOperationException("Cot "+table+"."+field.Key+" khong dung cau truc da kiem thu.");}
                    else if(requireAdded)throw new InvalidOperationException("Thieu cot "+table+"."+field.Key);
            }
        }
        async Task<Dictionary<string,string>> Fingerprints()
        {
            var result=new Dictionary<string,string>();
            foreach(var t in native)
            {
                var name=t.GetProperty("name").GetString()!;
                var remove=Added.TryGetValue(name,out var fields)?"-ARRAY["+string.Join(',',fields.Keys.Select(k=>"'"+k+"'"))+"]::text[]":"";
                result[name]=await Value<string>("SELECT md5(COALESCE(string_agg(rowdata::text,',' ORDER BY rowdata::text),'')) FROM(SELECT to_jsonb(t)"+remove+" AS rowdata FROM public."+Q(name)+" t) q");
            }
            return result;
        }
        await VerifyColumns(false);var before=await Fingerprints();
        await Exec(Regex.Replace(sql,@"(?m)^\s*(BEGIN;|COMMIT;)\s*$",""));
        await VerifyColumns(true);var after=await Fingerprints();
        var afterTables=await Value<string[]>("SELECT array_agg(tablename::text ORDER BY tablename) FROM pg_tables WHERE schemaname='public'");
        if(!before.OrderBy(p=>p.Key).SequenceEqual(after.OrderBy(p=>p.Key))||!beforeTables.SequenceEqual(afterTables))
            throw new InvalidOperationException("Du lieu/cau truc bang goc bi thay doi ngoai cac cot duoc phep; giao dich se hoan tac.");
        await tx.CommitAsync();
        await File.WriteAllTextAsync(reportFile,JsonSerializer.Serialize(new{AppliedAt=DateTimeOffset.UtcNow,SqlSha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sqlFile))),
            NativeTablesVerified=18,OriginalColumnDataUnchanged=true,TablesUnchanged=true,PublicTableCount=afterTables.Length,
            Columns=Added.SelectMany(t=>t.Value.Select(p=>new{Table=t.Key,Column=p.Key,Type=p.Value})).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
