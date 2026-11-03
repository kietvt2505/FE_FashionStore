using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class LoginAccountAudit
{
    public static async Task ReadAsync(string connectionString,string outputFile,string[] names)
    {
        var settings=new NpgsqlConnectionStringBuilder(connectionString){Pooling=false,Timeout=15,CommandTimeout=15,Timezone="UTC"};
        if(settings.Database!="postgres"||!((settings.Host??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)||(settings.Username??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Sai project Supabase da thong nhat.");
        if(names.Length is <1 or >10||names.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>254))throw new InvalidOperationException("Thong tin tra cuu khong hop le.");
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var readOnly=new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",c,tx))await readOnly.ExecuteNonQueryAsync();
        var results=new List<object>();
        foreach(var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found=false;var trimmed=name.Trim();
            var digits=Regex.IsMatch(trimmed,@"^\+?[0-9\s.-]{9,20}$")?Regex.Replace(trimmed,"[^0-9]",""):"";
            var phone=digits.StartsWith("84")&&digits.Length==11?"0"+digits[2..]:digits;
            await using(var cmd=new NpgsqlCommand("""
                SELECT k.makh,k.hoten,k.tendangnhap,k.trangthai,k.matkhau,
                    lower(btrim(COALESCE(k.email,'')))=lower(@name),
                    lower(btrim(COALESCE(k.tendangnhap,'')))=lower(@name),
                    (SELECT count(*) FROM public.hoadon h WHERE h.makh=k.makh)
                FROM public.khachhang k
                WHERE lower(btrim(k.tendangnhap))=lower(@name) OR lower(btrim(k.email))=lower(@name)
                   OR lower(regexp_replace(btrim(k.hoten),'[[:space:]]','','g'))=lower(@compact)
                   OR (@phone<>'' AND CASE
                        WHEN regexp_replace(k.sodienthoai,'[^0-9]','','g') LIKE '84%'
                          AND length(regexp_replace(k.sodienthoai,'[^0-9]','','g'))=11
                        THEN '0'||substr(regexp_replace(k.sodienthoai,'[^0-9]','','g'),3)
                        ELSE regexp_replace(k.sodienthoai,'[^0-9]','','g') END=@phone)
                """,c,tx))
            {
                cmd.Parameters.AddWithValue("name",trimmed);cmd.Parameters.AddWithValue("compact",Regex.Replace(trimmed,@"\s", ""));cmd.Parameters.AddWithValue("phone",phone);
                await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())
                {
                    found=true;var password=r.IsDBNull(4)?null:r.GetString(4);
                    results.Add(new{RequestedName=name,Found=true,CustomerId=r.GetString(0),FullName=r.GetString(1),
                        LoginName=r.IsDBNull(2)?null:r.GetString(2),Enabled=r.GetBoolean(3),PasswordPresent=!string.IsNullOrWhiteSpace(password),
                        PasswordFormat=Format(password),MatchedEmail=r.GetBoolean(5),MatchedLoginName=r.GetBoolean(6),OrderCount=r.GetInt64(7)});
                }
            }
            if(!found)
            {
                results.Add(new{RequestedName=name,Found=false});
            }
        }
        await tx.CommitAsync();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        await File.WriteAllTextAsync(outputFile,JsonSerializer.Serialize(new{CheckedAt=DateTimeOffset.UtcNow,ReadOnly=true,Accounts=results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("DA DOC TRANG THAI TAI KHOAN. Khong thay doi du lieu, khong xuat mat khau/hash.");
    }
    private static string Format(string? password)
    {
        if(string.IsNullOrWhiteSpace(password))return "missing";
        if(password.StartsWith("$2a$")||password.StartsWith("$2b$")||password.StartsWith("$2y$"))return "bcrypt";
        if(password.StartsWith("$argon2"))return "argon2";
        try{var bytes=Convert.FromBase64String(password);if(bytes.Length>=33&&bytes[0] is 0 or 1)return "identity-base64";}catch(FormatException){}
        return "unrecognized";
    }
}
