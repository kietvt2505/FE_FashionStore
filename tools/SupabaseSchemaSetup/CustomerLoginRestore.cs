using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FashionStore.SchemaSetup;

public static class CustomerLoginRestore
{
    private static string Phone(string value)
    {
        var digits=Regex.Replace(value,"[^0-9]","");
        return digits.StartsWith("84")&&digits.Length==11?"0"+digits[2..]:digits;
    }

    public static async Task RestoreAsync(string connectionString,string customerId,string loginName,string expectedEmail,
        string expectedPhone,string password,string reportFile,bool localTest=false)
    {
        var normalized=loginName.Trim().ToLowerInvariant();
        if(!Regex.IsMatch(normalized,"^[a-z0-9._-]{3,50}$")||string.IsNullOrWhiteSpace(customerId)||customerId.Length>20)
            throw new InvalidOperationException("Ma khach hang/ten dang nhap khong hop le.");
        if(password.Length is <12 or >128||!Regex.IsMatch(password,"[A-Za-z]")||!Regex.IsMatch(password,"[0-9]"))
            throw new InvalidOperationException("Mat khau moi can 12-128 ky tu, co chu va so.");
        if(string.IsNullOrWhiteSpace(expectedEmail)||Phone(expectedPhone).Length<9)
            throw new InvalidOperationException("Can email va so dien thoai doi chieu ho so.");
        var settings=new NpgsqlConnectionStringBuilder(connectionString){Pooling=false,Timeout=15,CommandTimeout=30,Timezone="UTC"};
        if(localTest)
        {
            if(settings.Host is not ("127.0.0.1" or "localhost" or "::1")||!(settings.Database??"").StartsWith("fashionstore_native18_",StringComparison.Ordinal))
                throw new InvalidOperationException("Sai database thu nghiem.");
        }
        else if(settings.Database!="postgres"||!((settings.Host??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)||(settings.Username??"").Contains("oagcfzcslkkiokbmkrlg",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Sai project Supabase da thong nhat.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        await using var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using(var guard=new NpgsqlCommand("SET LOCAL lock_timeout='5s';LOCK TABLE public.khachhang IN SHARE ROW EXCLUSIVE MODE",c,tx))await guard.ExecuteNonQueryAsync();
        string before;
        await using(var find=new NpgsqlCommand("""
            SELECT tendangnhap,matkhau,trangthai,email,sodienthoai,
                (to_jsonb(k)-ARRAY['tendangnhap','matkhau','makhoiphucbam','hethanmakhoiphuc','phienmatkhaukhoiphuc'])::text
            FROM public.khachhang k WHERE makh=@kh FOR UPDATE
            """,c,tx))
        {
            find.Parameters.AddWithValue("kh",customerId);await using var r=await find.ExecuteReaderAsync();
            if(!await r.ReadAsync())throw new InvalidOperationException("Khong tim thay ho so khach hang.");
            if(!r.GetBoolean(2))throw new InvalidOperationException("Ho so dang bi tat; khong tu dong kich hoat.");
            if(r.IsDBNull(3)||!string.Equals(r.GetString(3).Trim(),expectedEmail.Trim(),StringComparison.OrdinalIgnoreCase)||Phone(r.GetString(4))!=Phone(expectedPhone))
                throw new InvalidOperationException("Email/so dien thoai khong khop ho so da doi chieu.");
            if(!r.IsDBNull(0)&&!string.IsNullOrWhiteSpace(r.GetString(0))&&!string.Equals(r.GetString(0).Trim(),normalized,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ho so da co ten dang nhap khac. Khong ghi de.");
            if(!r.IsDBNull(1)&&!string.IsNullOrWhiteSpace(r.GetString(1)))throw new InvalidOperationException("Ho so da co mat khau. Khong ghi de.");
            before=r.GetString(5);
        }
        await using(var duplicate=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.khachhang WHERE makh<>@kh AND lower(btrim(tendangnhap))=@name)",c,tx))
        {
            duplicate.Parameters.AddWithValue("kh",customerId);duplicate.Parameters.AddWithValue("name",normalized);
            if(Convert.ToBoolean(await duplicate.ExecuteScalarAsync()))throw new InvalidOperationException("Ten dang nhap da duoc su dung boi ho so khac.");
        }
        var hasher=new PasswordHasher<object>(Options.Create(new PasswordHasherOptions{IterationCount=210000}));
        var hash=hasher.HashPassword(new object(),password);
        await using(var save=new NpgsqlCommand("""
            UPDATE public.khachhang SET tendangnhap=@name,matkhau=@hash,
                makhoiphucbam=NULL,hethanmakhoiphuc=NULL,phienmatkhaukhoiphuc=NULL
            WHERE makh=@kh AND (matkhau IS NULL OR btrim(matkhau)='')
            """,c,tx))
        {
            save.Parameters.AddWithValue("name",normalized);save.Parameters.AddWithValue("hash",hash);save.Parameters.AddWithValue("kh",customerId);
            if(await save.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Ho so da thay doi. Can doi chieu lai.");
        }
        await using(var verify=new NpgsqlCommand("""
            SELECT matkhau,(to_jsonb(k)-ARRAY['tendangnhap','matkhau','makhoiphucbam','hethanmakhoiphuc','phienmatkhaukhoiphuc'])::text
            FROM public.khachhang k WHERE makh=@kh
            """,c,tx))
        {
            verify.Parameters.AddWithValue("kh",customerId);await using var r=await verify.ExecuteReaderAsync();await r.ReadAsync();
            if(r.GetString(1)!=before||hasher.VerifyHashedPassword(new object(),r.GetString(0),password)==PasswordVerificationResult.Failed)
                throw new InvalidOperationException("Kiem tra bao toan ho so/mat khau khong dat; giao dich se hoan tac.");
        }
        await tx.CommitAsync();
        await File.WriteAllTextAsync(reportFile,JsonSerializer.Serialize(new{UpdatedAt=DateTimeOffset.UtcNow,CustomerId=customerId,
            LoginName=normalized,PasswordConfigured=true,ProfileDataUnchanged=true,PasswordFormat="ASP.NET Identity",CreatedNewCustomer=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("DA KHOI PHUC DANG NHAP TREN HO SO CU. Khong xuat mat khau/hash.");
    }
}
