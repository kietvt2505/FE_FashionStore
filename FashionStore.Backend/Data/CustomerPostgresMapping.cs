using FashionStore.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Backend.Data;

internal static class CustomerPostgresMapping
{
    internal static void Configure(ModelBuilder model)
    {
        model.Ignore<SanPham>(); model.Ignore<LoaiSanPham>(); model.Ignore<BienTheSanPham>();
        model.Ignore<HinhAnhSanPham>(); model.Ignore<NhanVien>(); model.Ignore<PhanQuyen>(); model.Ignore<HoaDonAdmin>();
        var customer = model.Entity<KhachHang>();
        customer.ToTable("khachhang", "public");
        customer.HasKey(c => c.MaKH);
        customer.Ignore(c => c.RowVer); customer.Ignore(c => c.NgayTao);
        customer.Property(c => c.PgVersion).IsRowVersion().HasColumnName("xmin");
        var account = model.Entity<TaiKhoanKhachHang>();
        account.ToTable("khachhang", "public"); account.HasKey(a => a.MaKH);
        account.Ignore(a => a.NgayTao); account.Ignore(a => a.DangNhapCuoi);
        account.Property<uint>("PgVersion").IsRowVersion().HasColumnName("xmin");
        account.HasIndex(a => a.TenDangNhap).IsUnique();
        account.HasOne(a => a.Customer).WithOne().HasForeignKey<TaiKhoanKhachHang>(a => a.MaKH).OnDelete(DeleteBehavior.Restrict);
        model.Ignore<GioHang>(); model.Ignore<ChiTietGioHang>();
        var product = model.Entity<SupabaseProduct>();
        product.ToTable("sanpham", "public"); product.HasKey(p => p.masku);
        product.Property(p => p.dongia).HasPrecision(18, 2);
        product.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.maloai);
        var category = model.Entity<SupabaseCategory>();
        category.ToTable("loaisanpham", "public"); category.HasKey(c => c.maloai);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
            {
                if (property.Name != nameof(KhachHang.PgVersion)) property.SetColumnName(property.Name.ToLowerInvariant());
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    property.SetColumnType("timestamp without time zone");
            }
        account.Property(a => a.TenDangNhap).HasColumnName("tendangnhap").HasMaxLength(50).IsRequired();
        account.Property(a => a.MatKhauHash).HasColumnName("matkhau").HasMaxLength(200).IsRequired(false);
    }
}
