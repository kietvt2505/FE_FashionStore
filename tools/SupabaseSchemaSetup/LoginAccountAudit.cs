using System.Text.Json;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class LoginAccountAudit
{
    public static async Task ReadAsync(string connectionString,string outputFile,string[] names)
    {
        var settings=new NpgsqlConnectionStringBuilder(connectionString){Pooling=false,Timeout=15,CommandTimeout=15,Timezone="UTC"};
        if(settings.Database!="postgres"||!((settings.Host??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)||(settings.Username??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Sai project Supabase da thong nhat.");
        if(names.Length is <1 or >10||names.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>100))throw new InvalidOperationException("Ten tai khoan khong hop le.");
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await using(var readOnly=new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",c,tx))await readOnly.ExecuteNonQueryAsync();
        var results=new List<object>();
        foreach(var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found=false;
            await using(var cmd=new NpgsqlCommand("SELECT tendangnhap,trangthai,matkhau FROM public.khachhang WHERE lower(btrim(tendangnhap))=lower(@name)",c,tx))
            {
                cmd.Parameters.AddWithValue("name",name.Trim());await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())
                {
                    found=true;var password=r.IsDBNull(2)?null:r.GetString(2);
                    results.Add(new{RequestedName=name,Found=true,LoginName=r.GetString(0),Enabled=r.GetBoolean(1),PasswordPresent=!string.IsNullOrWhiteSpace(password),PasswordFormat=Format(password)});
                }
            }
            if(!found)
            {
                await using var cmd=new NpgsqlCommand("SELECT tendangnhap,trangthai,matkhau IS NOT NULL AND btrim(matkhau)<>'' FROM public.khachhang WHERE lower(btrim(hoten))=lower(@name)",c,tx);
                cmd.Parameters.AddWithValue("name",name.Trim());await using var r=await cmd.ExecuteReaderAsync();var displayMatches=new List<object>();
                while(await r.ReadAsync())displayMatches.Add(new{LoginName=r.IsDBNull(0)?null:r.GetString(0),Enabled=r.GetBoolean(1),PasswordPresent=r.GetBoolean(2)});
                results.Add(new{RequestedName=name,Found=false,DisplayNameMatches=displayMatches});
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
