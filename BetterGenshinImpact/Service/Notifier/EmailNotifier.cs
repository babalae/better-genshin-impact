using System.IO;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using MailKit.Security;
using MimeKit;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Service.Notifier
{
    public class EmailNotifier : INotifier
    {
        // 发件人配置
        private readonly string _fromEmail;
        private readonly string _fromName;
        private readonly string _smtpPassword;
        private readonly int _smtpPort;

        // SMTP服务器配置
        private readonly string _smtpServer;
        private readonly string _smtpUsername;

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
        }

        // 收件人邮箱
        public string ToEmail { get; set; }
        public string Name { get; set; } = "Email";

        public async Task SendAsync(BaseNotificationData content)
        {
            if (string.IsNullOrEmpty(ToEmail))
            {
                throw new NotifierException("收件人邮箱地址为空");
            }

            // 创建邮件消息
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromEmail));
            message.To.Add(new MailboxAddress("", ToEmail));
            message.Subject = FormatEmailSubject(content);

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = FormatEmailBody(content)
            };

            // 添加图片附件（如果存在）
            if (content.Screenshot != null)
            {
                using var memoryStream = new MemoryStream();
                // 将图片保存到内存流
                await content.Screenshot.SaveAsJpegAsync(memoryStream);
                memoryStream.Position = 0; // 重置流位置
                    
                // 添加附件
                var attachment = await bodyBuilder.Attachments.AddAsync("screenshot.jpg", memoryStream, ContentType.Parse("image/jpeg"));
                attachment.ContentId = "screenshot";
            }

            message.Body = bodyBuilder.ToMessageBody();

            // 使用 MailKit 发送邮件
            using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
            try
            {
                // 根据端口选择合适的连接方式
                var secureSocketOptions = GetSecureSocketOptions();
                    
                await smtpClient.ConnectAsync(_smtpServer, _smtpPort, secureSocketOptions);
                    
                // 如果服务器需要认证，则进行登录
                if (!string.IsNullOrEmpty(_smtpUsername))
                {
                    await smtpClient.AuthenticateAsync(_smtpUsername, _smtpPassword);
                }

                await smtpClient.SendAsync(message);
                await smtpClient.DisconnectAsync(true);
            }
            catch (System.Exception ex)
            {
                var errorMessage = $"发送邮件失败: {ex.Message}";
                throw new NotifierException(errorMessage);
            }
        }

        /// <summary>
        /// 根据端口号选择合适的 SecureSocketOptions
        /// </summary>
        private SecureSocketOptions GetSecureSocketOptions()
        {
            // 465 端口通常使用 SSL/TLS，587 端口通常使用 StartTls
            return _smtpPort switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                587 => SecureSocketOptions.StartTls,
                25 => SecureSocketOptions.None, // 25 端口通常不加密
                _ => SecureSocketOptions.Auto // 其他端口自动选择
            };
        }

        private string FormatEmailSubject(BaseNotificationData content)
        {
            // 可以根据实际需求自定义邮件主题格式
            return $"通知 - {content.GetType().Name}";
        }

        private string FormatEmailBody(BaseNotificationData content)
        {
            var builder = new StringBuilder();
            builder.AppendLine("<html><body style='font-family: Arial, sans-serif;'>");

            // 添加通知标题
            builder.AppendLine("<h2 style='color: #333;'>通知详情</h2>");

            // 添加通知内容
            foreach (var prop in content.GetType().GetProperties())
            {
                // 跳过Screenshot属性，它会单独处理
                if (prop.Name == "Screenshot")
                    continue;

                var value = prop.GetValue(content);
                if (value != null)
                {
                    builder.AppendFormat("<p><strong>{0}:</strong> {1}</p>", prop.Name, value);
                }
            }

            // 添加提示信息
            if (content.Screenshot != null)
            {
                builder.AppendLine("<p><em>截图已作为附件添加到邮件中。</em></p>");
            }
            
            builder.AppendLine("</body></html>");
            return builder.ToString();
        }
    }
}