namespace FashionStore.Backend.Services;

// Values persisted in the partner's existing tables, matching WinForms 6d11b10.
public static class NativeStoreCodes
{
    public const string Cod="COD",Transfer="CHUYEN_KHOAN",TransferPending="WEB_CHO_THANH_TOAN";
    public const string Confirmed="Vừa xác nhận đơn",WaitingPayment="Chờ xác nhận thanh toán";
    public const string Unpaid="CHUA_THANH_TOAN",Paid="DA_THANH_TOAN";
    public const string Refund="HOAN_TIEN",Exchange="DOI_SAN_PHAM";
    public static string? ReturnKind(string kind)=>kind switch{"TRA" or Refund=>Refund,"DOI" or Exchange=>Exchange,_=>null};
    public static string ShippingFor(string method)=>method==Cod?Confirmed:WaitingPayment;
}
