using System.Net;
using System.Net.Mail;

namespace FashionStore.Backend.Services;

public interface IRecoveryEmailSender
{
    Task SendAsync(string from,string recipient,string link,CancellationToken ct);
}

public sealed class SmtpRecoveryEmailSender(IConfiguration config):IRecoveryEmailSender
{
    public async Task SendAsync(string from,string recipient,string link,CancellationToken ct)
    {
        using var smtp=new SmtpClient(config["Recovery:SmtpHost"]!,config.GetValue<int?>("Recovery:SmtpPort")??587){EnableSsl=true};
        if(!string.IsNullOrWhiteSpace(config["Recovery:SmtpUser"]))
            smtp.Credentials=new NetworkCredential(config["Recovery:SmtpUser"],config["Recovery:SmtpPassword"]);
        using var message=new MailMessage(from,recipient,"YODY — khôi phục mật khẩu",
            "Liên kết có hiệu lực 20 phút, dùng một lần:\n"+link+"\nNếu bạn không yêu cầu, hãy bỏ qua email này.");
        await smtp.SendMailAsync(message,ct);
    }
}
