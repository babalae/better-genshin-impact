using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.GameTask.SkillCd;
using BetterGenshinImpact.Helpers.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class SkillCdConfigWindow : FluentWindow
{
    public ObservableCollection<SkillCdRule> Rules { get; set; }

    public SkillCdConfigWindow(List<SkillCdRule> initialRules)
    {
        InitializeComponent();

        // 深度复制列表，避免直接修改原配置
        Rules = new ObservableCollection<SkillCdRule>();
        if (initialRules != null)
        {
            foreach (var rule in initialRules)
            {
                Rules.Add(new SkillCdRule
                {
                    RoleName = rule.RoleName,
                    CdValueText = rule.CdValueText
                });
            }
        }

        // 如果为空，添加默认行
        if (Rules.Count == 0)
        {
            Rules.Add(new SkillCdRule());
        }

        DataContext = this;

        this.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SkillCdRule rule)
        {
            Rules.Remove(rule);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Rules.Add(new SkillCdRule());
    }

    public List<SkillCdRule> GetValidRules()
    {
        return Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.RoleName))
            .Select(r => new SkillCdRule
            {
                RoleName = r.RoleName.Trim(),
                CdValueText = r.CdValueText?.Trim()
            })
            .ToList();
    }
}
