using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Windows.Markup;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.View.Controls.Resin;
using BetterGenshinImpact.ViewModel.Resin;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.UnitTest.ViewModelTests.ResinTests;

/// <summary>在现有专属设置测试的 STA、Application、主题及绑定监听器中执行，不新建 Application 或 FluentWindow。</summary>
internal static class ResinSettingsViewAssertions
{
    /// <summary>每种主题检查四能力乘三方式、合成方式、卡片警告与三套通用字段模板。</summary>
    internal static void VerifyCurrentTheme()
    {
        foreach (var capability in new[] { ResinCapabilities.Domain, ResinCapabilities.StygianOnslaught, ResinCapabilities.LeyLineOutcrop, ResinCapabilities.Boss })
        foreach (var mode in Enum.GetValues<ResinUsageMode>())
        {
            var value = capability.Defaults;
            value.Mode = mode;
            value.Counts = new() { Condensed = 2, Original = 4 };
            var vm = new ResinUsageEditorViewModel("测试任务", capability, value, _ => Task.FromResult(true));
            var view = new ResinUsageEditorView(vm);
            CheckLayout(view);
            Assert.Equal(value, vm.BuildValue());
            Assert.Equal(4, Descendants(view).OfType<ToggleSwitch>().Count());
            Assert.Equal(3, Descendants(view).OfType<RadioButton>().Count());
            Assert.All(Descendants(view).OfType<ToggleSwitch>(), control => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))));
        }
        foreach (var amount in new[] { 0, 40 })
        {
            var vm = new CraftResinEditorViewModel(amount);
            var view = new CraftResinEditorView(vm);
            CheckLayout(view);
            Assert.Equal(amount, vm.BuildValue());
            if (amount == 0)
            {
                vm.KeepResin = true;
                CheckLayout(view);
                Assert.Equal(40, vm.KeepAmount);
            }
        }
        // 无效的当前字段和隐藏字段也不能在控件初始化时被范围校正静默写回。
        var invalid = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 0, Counts = new() { Condensed = 1000 } };
        var invalidVm = new ResinUsageEditorViewModel("测试任务", ResinCapabilities.Domain, invalid, _ => Task.FromResult(true));
        CheckLayout(new ResinUsageEditorView(invalidVm));
        Assert.Equal(invalid, invalidVm.BuildValue());
        Assert.False(invalidVm.CanConfirm);
        foreach (var problem in new[] { false, true }) CheckCard(problem);
        CheckGenericTemplates();
    }

    /// <summary>检查依赖属性绑定没有覆盖宿主 DataContext，并且警告同时包含图标和原因。</summary>
    private static void CheckCard(bool problem)
    {
        var value = new ResinUsage { Mode = problem ? ResinUsageMode.PerType : ResinUsageMode.Exhaust };
        var host = new ResinUsageEntryViewModel("秘境", ResinCapabilities.Domain, value, null);
        var card = new ResinSettingsCard { DataContext = host };
        card.SetBinding(ResinSettingsCard.SummaryProperty, new Binding(nameof(host.Summary)) { Mode = BindingMode.OneWay });
        card.SetBinding(ResinSettingsCard.ProblemProperty, new Binding(nameof(host.Problem)) { Mode = BindingMode.OneWay });
        card.SetBinding(ResinSettingsCard.CommandProperty, new Binding(nameof(host.EditCommand)) { Mode = BindingMode.OneWay });
        CheckLayout(card);
        Assert.Same(host, card.DataContext);
        var action = Assert.Single(Descendants(card).OfType<CardAction>());
        Assert.Same(host, action.DataContext);
        Assert.Same(host.EditCommand, action.Command);
        Assert.Equal("树脂使用：" + (host.Problem ?? host.Summary), AutomationProperties.GetName(card));
        Assert.Equal(card.AutomationName, AutomationProperties.GetName(action));
        Assert.Single(Descendants(card).OfType<System.Windows.Controls.TextBlock>()
            .Where(text => text.Text == host.Summary && text.Visibility == Visibility.Visible));
        var warning = Assert.Single(Descendants(card).OfType<SymbolIcon>().Where(icon => icon.Symbol == SymbolRegular.Warning24));
        Assert.Equal(problem ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)warning.Parent).Visibility);
        if (problem) Assert.Contains(host.Problem!, AutomationProperties.GetName(action));
        host.Reset(new() { Mode = ResinUsageMode.RunCount, RunCount = 6 });
        CheckLayout(card);
        Assert.Equal(host.Summary, card.Summary);
        Assert.True(BindingOperations.IsDataBound(card, ResinSettingsCard.SummaryProperty));
    }

    /// <summary>三套参数模板都实际实例化树脂卡片，不创建包含它们的 FluentWindow。</summary>
    private static void CheckGenericTemplates()
    {
        var modern = new ResourceDictionary { Source = new Uri("/BetterGI;component/View/Controls/PuloniaOneDragonParameterResources.xaml", UriKind.Relative) };
        var legacy = new ResourceDictionary { Source = new Uri("/BetterGI;component/View/Controls/PuloniaOneDragonResources.xaml", UriKind.Relative) };
        var creation = ReadCreationTemplate();
        foreach (var taskType in new[] { "solo.auto_domain", "solo.auto_boss", "solo.auto_stygian_onslaught", "solo.auto_ley_line_outcrop" })
        {
            var capability = ResinUsageViewModelSupport.GetTask(taskType)!.Value.Capability;
            var schema = PuloniaOneDragonSettingChoices.Decorate("resin_usage", PuloniaSoloTaskSchema.Create(typeof(ResinUsage)), null, taskType);
            var value = JObject.Parse(BetterGenshinImpact.GameTask.SoloTask.SoloTaskJson.Serialize(capability.Defaults));
            foreach (var (template, field) in new (DataTemplate, object)[]
            {
                ((DataTemplate)modern["OneDragonParameterFieldTemplate"], new PuloniaOneDragonParameterFieldViewModel("resin_usage", schema, value)),
                ((DataTemplate)legacy["ParameterFieldTemplate"], new PuloniaTaskParameterFieldViewModel("resin_usage", schema, value, true, false)),
                (creation, new PuloniaTaskParameterFieldViewModel("resin_usage", schema, value, true, false))
            })
            {
                var view = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
                view.DataContext = field;
                CheckLayout(view, false);
                Assert.Single(Descendants(view).OfType<ResinSettingsCard>());
            }
        }
    }

    /// <summary>只读取创建窗口的字段模板与命名空间，以同一份 XAML 验证新形态，避免构造窗口和第二个 Application。</summary>
    private static DataTemplate ReadCreationTemplate([CallerFilePath] string sourceFile = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../BetterGenshinImpact/View/Windows/Pulonia/PuloniaTaskCreationDialog.xaml"));
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var template = document.Descendants(presentation + "DataTemplate").Single(element => (string?)element.Attribute(xaml + "Key") == "ParameterFieldTemplate");
        // 源文件局部命名空间依赖编译程序集；动态读取模板时显式标注 BetterGI，不能解析到测试程序集。
        var namespaces = document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration)
            .Select(attribute => new XAttribute(attribute.Name, attribute.Value.StartsWith("clr-namespace:BetterGenshinImpact.", StringComparison.Ordinal)
                ? attribute.Value + ";assembly=BetterGI" : attribute.Value));
        var copy = new XElement(template);
        foreach (var element in copy.DescendantsAndSelf().Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:BetterGenshinImpact.", StringComparison.Ordinal)))
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=BetterGI");
        var dictionary = new XElement(presentation + "ResourceDictionary", namespaces, copy);
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        return (DataTemplate)resources["ParameterFieldTemplate"];
    }

    /// <summary>以弹窗的 480 宽布局并处理模板和绑定队列，检查数字框、下拉框边界及统一规格。</summary>
    private static void CheckLayout(FrameworkElement view, bool checkEditorSpecs = true)
    {
        const double width = 480;
        for (var round = 0; round < 4; round++)
        {
            view.Measure(new Size(width, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
        Assert.Equal(width, view.ActualWidth);
        Assert.True(view.ActualHeight > 30);
        foreach (var control in Descendants(view).OfType<Control>().Where(control => control is NumberBox or ComboBox))
        {
            if (control.ActualWidth > 0)
            {
                var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                Assert.True(bounds.Right <= width + 1 && bounds.Left >= -1, $"{control.GetType().Name} 超出树脂编辑区域：{bounds}");
            }
            if (!checkEditorSpecs) continue;
            if (control is NumberBox number)
            {
                Assert.Equal(150, number.Width);
                Assert.Equal(1, number.SmallChange);
                Assert.Equal(5, number.LargeChange);
                Assert.False(number.ClearButtonEnabled);
                Assert.Equal("Inline", number.SpinButtonPlacementMode.ToString());
                Assert.Equal("InvalidInputOverwritten", number.ValidationMode.ToString());
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(number)));
            }
            else Assert.Equal(150, control.Width);
        }
    }

    /// <summary>枚举模板实例化后的可视树。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
