using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
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

            // 忽略SSL证书验证错误
            ServicePointManager.ServerCertificateValidationCallback =
                delegate { return true; };
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

            // 创建一个新的SmtpClient实例（不复用）
            using (var smtpClient = new SmtpClient())
            {
                try
                {
                    // 配置SMTP客户端
                    smtpClient.Host = _smtpServer;
                    smtpClient.Port = _smtpPort;
                    smtpClient.EnableSsl = true;
                    smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
                    smtpClient.Timeout = 30000; // 30秒超时

                    // 创建邮件
                    using (var mailMessage = new MailMessage())
                    {
                        mailMessage.From = new MailAddress(_fromEmail, _fromName);
                        mailMessage.To.Add(ToEmail);
                        mailMessage.Subject = FormatEmailSubject(content);
                        mailMessage.Body = FormatEmailBody(content);
                        mailMessage.IsBodyHtml = true;
                        mailMessage.BodyEncoding = Encoding.UTF8;
                        mailMessage.SubjectEncoding = Encoding.UTF8;

                        // 添加图片附件（如果存在）
                        if (content.Screenshot != null)
                        {
                            var tempPath = Path.GetTempFileName() + ".jpg";
                            try
                            {
                                // 保存图片到临时文件
                                await content.Screenshot.SaveAsJpegAsync(tempPath);

                                // 从文件添加附件
                                var attachment = new Attachment(tempPath);
                                mailMessage.Attachments.Add(attachment);

                                // 发送邮件
                                await smtpClient.SendMailAsync(mailMessage);

                                // 清理附件和临时文件
                                attachment.Dispose();
                                if (File.Exists(tempPath)) File.Delete(tempPath);
                            }
                            catch (System.Exception ex)
                            {
                                // 尝试清理临时文件
                                try
                                {
                                    if (File.Exists(tempPath)) File.Delete(tempPath);
                                }
                                catch
                                {
                                    /* 忽略清理错误 */
                                }

                                throw new NotifierException($"发送邮件失败: {ex.Message}");
                            }
                        }
                        else
                        {
                            // 没有图片时直接发送
                            await smtpClient.SendMailAsync(mailMessage);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    var errorMessage = $"发送邮件失败: {ex.Message}";
                    throw new NotifierException(errorMessage);
                }
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
            builder.AppendLine("<p><em>如有截图，请查看附件。</em></p>");
            builder.AppendLine("</body></html>");
            return builder.ToString();
        }
    }
}
