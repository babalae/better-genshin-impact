using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 通知配置
    /// </summary>
    [Serializable]
    public partial class NotificationConfig : ObservableObject
    {
        #region Webhook配置

        /// <summary>
        /// 是否启用Webhook通知
        /// </summary>
        [ObservableProperty]
        private bool _webhookEnabled;

        /// <summary>
        /// Webhook端点
        /// </summary>
        [ObservableProperty]
        private string _webhookEndpoint = string.Empty;

        #endregion

        #region 邮件配置

        /// <summary>
        /// 是否启用邮件通知
        /// </summary>
        [ObservableProperty]
        private bool _emailEnabled;

        /// <summary>
        /// SMTP服务器设置
        /// </summary>
        [ObservableProperty]
        private string _smtpServer = string.Empty;

        /// <summary>
        /// SMTP端口(默认587)
        /// </summary>
        [ObservableProperty]
        private int _smtpPort = 587;

        /// <summary>
        /// SMTP用户名
        /// </summary>
        [ObservableProperty]
        private string _smtpUsername = string.Empty;

        /// <summary>
        /// SMTP密码
        /// </summary>
        [ObservableProperty]
        private string _smtpPassword = string.Empty;

        /// <summary>
        /// 启用SSL/TLS
        /// </summary>
        [ObservableProperty]
        private bool _enableSsl = true;

        /// <summary>
        /// 发件人邮箱
        /// </summary>
        [ObservableProperty]
        private string _fromEmail = string.Empty;

        /// <summary>
        /// 发件人显示名称
        /// </summary>
        [ObservableProperty]
        private string _fromName = "原神助手";

        /// <summary>
        /// 收件人邮箱列表(逗号分隔)
        /// </summary>
        [ObservableProperty]
        private string _toEmail = string.Empty;

        #endregion

        #region 通知设置

        /// <summary>
        /// 任务完成时通知
        /// </summary>
        [ObservableProperty]
        private bool _notifyOnTaskComplete = true;

        /// <summary>
        /// 出现错误时通知
        /// </summary>
        [ObservableProperty]
        private bool _notifyOnError = true;

        /// <summary>
        /// 原神启动时通知
        /// </summary>
        [ObservableProperty]
        private bool _notifyOnGameStart;

        /// <summary>
        /// 重要提醒时通知
        /// </summary>
        [ObservableProperty]
        private bool _notifyOnImportant = true;

        #endregion

        public NotificationConfig()
        {
            // 注册属性变更事件
            PropertyChanged += (_, _) => OnConfigChanged();
        }

        #region 配置验证

        /// <summary>
        /// 验证Webhook配置
        /// </summary>
        public bool ValidateWebhookConfig()
        {
            if (!WebhookEnabled) return true;
            return !string.IsNullOrEmpty(WebhookEndpoint);
        }

        /// <summary>
        /// 获取Webhook配置验证错误信息
        /// </summary>
        public string GetWebhookValidationError()
        {
            if (!WebhookEnabled) return string.Empty;
            if (string.IsNullOrEmpty(WebhookEndpoint))
                return "Webhook端点不能为空";
            return string.Empty;
        }

        /// <summary>
        /// 验证邮件配置
        /// </summary>
        public bool ValidateEmailConfig()
        {
            if (!EmailEnabled) return true;

            // 验证SMTP设置
            if (string.IsNullOrEmpty(SmtpServer)) return false;
            if (SmtpPort <= 0 || SmtpPort > 65535) return false;
            if (string.IsNullOrEmpty(SmtpUsername)) return false;
            if (string.IsNullOrEmpty(SmtpPassword)) return false;

            // 验证邮件地址
            if (string.IsNullOrEmpty(FromEmail)) return false;
            if (string.IsNullOrEmpty(ToEmail)) return false;

            return true;
        }

        /// <summary>
        /// 获取邮件配置验证错误信息
        /// </summary>
        public string GetEmailValidationError()
        {
            if (!EmailEnabled) return string.Empty;

            // SMTP设置验证
            if (string.IsNullOrEmpty(SmtpServer))
                return "SMTP服务器地址不能为空";
            if (SmtpPort <= 0 || SmtpPort > 65535)
                return "SMTP端口无效(1-65535)";
            if (string.IsNullOrEmpty(SmtpUsername))
                return "SMTP用户名不能为空";
            if (string.IsNullOrEmpty(SmtpPassword))
                return "SMTP密码不能为空";

            // 邮件地址验证
            if (string.IsNullOrEmpty(FromEmail))
                return "发件人邮箱不能为空";
            if (string.IsNullOrEmpty(ToEmail))
                return "收件人邮箱不能为空";

            return string.Empty;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取收件人列表
        /// </summary>
        public List<string> GetRecipientList()
        {
            var recipients = new List<string>();
            if (!string.IsNullOrEmpty(ToEmail))
            {
                var emails = ToEmail.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var email in emails)
                {
                    var trimmedEmail = email.Trim();
                    if (!string.IsNullOrEmpty(trimmedEmail))
                    {
                        recipients.Add(trimmedEmail);
                    }
                }
            }
            return recipients;
        }

        /// <summary>
        /// 配置变更处理
        /// </summary>
        private void OnConfigChanged()
        {
            // 这里可以添加配置变更时的处理逻辑
            // 例如: 更新通知服务状态
        }

        #endregion

        #region 配置导入导出

        /// <summary>
        /// 导出配置(不包含敏感信息)
        /// </summary>
        public NotificationConfig ExportConfig()
        {
            var config = new NotificationConfig
            {
                // Webhook设置
                WebhookEnabled = WebhookEnabled,
                WebhookEndpoint = WebhookEndpoint,

                // 邮件基本设置
                EmailEnabled = EmailEnabled,
                SmtpServer = SmtpServer,
                SmtpPort = SmtpPort,
                SmtpUsername = SmtpUsername,
                EnableSsl = EnableSsl,
                FromEmail = FromEmail,
                FromName = FromName,
                ToEmail = ToEmail,

                // 通知设置
                NotifyOnTaskComplete = NotifyOnTaskComplete,
                NotifyOnError = NotifyOnError,
                NotifyOnGameStart = NotifyOnGameStart,
                NotifyOnImportant = NotifyOnImportant
            };

            return config;
        }

        /// <summary>
        /// 从其他配置导入(可选择是否包含敏感信息)
        /// </summary>
        public void ImportConfig(NotificationConfig other, bool includeSensitive = false)
        {
            // Webhook设置
            WebhookEnabled = other.WebhookEnabled;
            WebhookEndpoint = other.WebhookEndpoint;

            // 邮件基本设置
            EmailEnabled = other.EmailEnabled;
            SmtpServer = other.SmtpServer;
            SmtpPort = other.SmtpPort;
            SmtpUsername = other.SmtpUsername;
            if (includeSensitive)
            {
                SmtpPassword = other.SmtpPassword;
            }
            EnableSsl = other.EnableSsl;
            FromEmail = other.FromEmail;
            FromName = other.FromName;
            ToEmail = other.ToEmail;

            // 通知设置
            NotifyOnTaskComplete = other.NotifyOnTaskComplete;
            NotifyOnError = other.NotifyOnError;
            NotifyOnGameStart = other.NotifyOnGameStart;
            NotifyOnImportant = other.NotifyOnImportant;
        }

        #endregion
    }
}
