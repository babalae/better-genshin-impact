using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier
{
    public class EmailNotifier : INotifier
    {
        public string Name { get; set; } = "Email";
        
        // SMTP服务器配置
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        
        // 发件人配置
        private readonly string _fromEmail;
        private readonly string _fromName;
        
        // 收件人邮箱
        public string ToEmail { get; set; }

        // 提升 SmtpClient 为类的成员变量
        private readonly SmtpClient _smtpClient;

        public EmailNotifier(
            string smtpServer,
            int smtpPort,
            string smtpUsername,
            string smtpPassword,
            string fromEmail,
            string fromName,
            string toEmail = "")
        {
            _smtpServer = smtpServer;
            _smtpPort = smtpPort;
            _smtpUsername = smtpUsername;
            _smtpPassword = smtpPassword;
            _fromEmail = fromEmail;
            _fromName = fromName;
            ToEmail = toEmail;

            // 在构造函数中初始化 SmtpClient
            _smtpClient = new SmtpClient(_smtpServer, _smtpPort)
            {
                Credentials = new System.Net.NetworkCredential(_smtpUsername, _smtpPassword),
                EnableSsl = true
            };
        }

        public async Task SendAsync(BaseNotificationData content)
        {
            if (string.IsNullOrEmpty(ToEmail))
            {
                throw new NotifierException("收件人邮箱地址为空");
            }

            try
            {
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = FormatEmailSubject(content),
                    Body = FormatEmailBody(content),
                    IsBodyHtml = true
                };
                
                mailMessage.To.Add(ToEmail);

                // 使用成员变量 _smtpClient 发送邮件
                await _smtpClient.SendMailAsync(mailMessage);
            }
            catch (System.Exception ex)
            {
                throw new NotifierException($"发送邮件失败: {ex.Message}");
            }
        }

        private string FormatEmailSubject(BaseNotificationData content)
        {
            // 可以根据实际需求自定义邮件主题格式
            return $"通知 - {content.GetType().Name}";
        }

        private string FormatEmailBody(BaseNotificationData content)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<html><body>");
            
            // 添加通知标题
            builder.AppendLine("<h2>通知详情</h2>");
            
            // 添加通知内容
            foreach (var prop in content.GetType().GetProperties())
            {
                var value = prop.GetValue(content);
                if (value != null)
                {
                    builder.AppendLine($"<p><strong>{prop.Name}:</strong> {value}</p>");
                }
            }
            
            builder.AppendLine("</body></html>");
            return builder.ToString();
        }
    }
}