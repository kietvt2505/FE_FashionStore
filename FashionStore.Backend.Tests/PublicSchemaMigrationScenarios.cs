using System.Net;
using Npgsql;
using Xunit;

namespace FashionStore.Backend.Tests;

internal static class PublicSchemaMigrationScenarios
{
    internal static async Task VerifyAsync(HttpClient alice,HttpClient bob,NpgsqlConnection sql)
    {
        // Recreate the deployed legacy layout from the complete, populated local test installation.
        // Native partner tables remain public. Exercise the actual migration against every website feature.
        string[] tables=["diachikhachhang","giohang","chitietgiohang","thongtinsanpham","sothutudonhangtheongay",
            "yeucauthanhtoantructuyen","biennhanthanhtoantructuyen","lichsugopgiohang","lichsuhuydonhang","chitietdonhanglucmua",
            "uudaidonhang","yeucauhoantien","thongtindonhang","yeucaukhoiphucmatkhau","xacnhanthanhtoanchuyenkhoan",
            "giakhuyenmaisanpham","hinhanhsanpham","danhgiasanpham","yeucaudoitra","sanphamyeuthich"];
        string[] views=["makhuyenmaihople","danhsachhoantienkhachhang","giabanthuctesanpham"];
        string[] routines=["allocate_order_number","can_cancel_order","cancel_customer_order","capture_cancelled_order_payment",
            "complete_customer_refund","confirm_bank_payment","guard_cancelled_web_order","is_product_promotion",
            "merge_guest_cart","set_product_discount","settle_gateway_payment","submit_refund_account"];
        string[] sequences=["diachikhachhang_id_seq","giohang_magiohang_seq","payos_payment_number","hinhanhsanpham_id_seq","danhgiasanpham_id_seq","web_order_number"];
        async Task Exec(string text){await using var cmd=new NpgsqlCommand(text,sql){CommandTimeout=120};await cmd.ExecuteNonQueryAsync();}
        async Task<T> Value<T>(string text){await using var cmd=new NpgsqlCommand(text,sql);return (T)(await cmd.ExecuteScalarAsync())!;}
        var definitions=new List<(string Name,string Args,string Definition)>();
        await using(var query=new NpgsqlCommand("SELECT proname,pg_get_function_identity_arguments(oid),pg_get_functiondef(oid) FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname=ANY(@names)",sql))
        {
            query.Parameters.AddWithValue("names",routines);
            await using var reader=await query.ExecuteReaderAsync();
            while(await reader.ReadAsync())definitions.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2)));
        }
        var nativeCount=await Value<long>("SELECT count(*) FROM public.sanpham");
        await Exec("CREATE SCHEMA webstore;REVOKE ALL ON SCHEMA webstore FROM PUBLIC;");
        foreach(var name in tables)await Exec($"ALTER TABLE public.{name} SET SCHEMA webstore");
        foreach(var name in views)await Exec($"ALTER VIEW public.{name} SET SCHEMA webstore");
        foreach(var name in sequences)
            if(await Value<bool>($"SELECT to_regclass('public.{name}') IS NOT NULL"))await Exec($"ALTER SEQUENCE public.{name} SET SCHEMA webstore");
        foreach(var routine in definitions)await Exec($"ALTER FUNCTION public.{routine.Name}({routine.Args}) SET SCHEMA webstore");
        foreach(var routine in definitions)
        {
            var legacy=routine.Definition;
            foreach(var name in tables.Concat(views).Concat(routines).Concat(sequences).OrderByDescending(n=>n.Length))
                legacy=legacy.Replace("public."+name,"webstore."+name);
            await Exec(legacy);
        }
        Assert.Equal(13,definitions.Count);
        await Exec(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Supabase-Move-Webstore-To-Public.sql")));
        Assert.True(await Value<bool>("SELECT to_regnamespace('webstore') IS NULL"));
        Assert.Equal(nativeCount,await Value<long>("SELECT count(*) FROM public.sanpham"));
        Assert.Equal(20L,await Value<long>("SELECT count(*) FROM public.web_schema_migration_20261102 WHERE row_count IS NOT NULL"));
        foreach(var path in new[]{"/api/account/me","/api/cart","/api/orders","/api/customer/returns","/api/customer/wishlist","/api/customer/addresses","/api/customer/reviews/P1/mine","/api/customer/reviews/P1/eligible-orders","/api/products","/api/customer/promotions"})
        {
            Assert.Equal(HttpStatusCode.OK,(await alice.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await bob.GetAsync(path)).StatusCode);
        }
        await Exec("UPDATE public.hoadon SET ghichugiaohang=ghichugiaohang");
    }
}
