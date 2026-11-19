using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class StartupHook
{
    private static readonly object SyncRoot = new object();
    private static readonly HashSet<object> HookedMetadatas = new HashSet<object>();
    private static readonly HashSet<int> HookedCoreWebViews = new HashSet<int>();
    private static readonly ConditionalWeakTable<object, object> ConfiguredCombos = new ConditionalWeakTable<object, object>();

    private static Dictionary<string, string> Dict;
    private static bool DictLoaded;

    private static bool WpfBaseHooked;
    private static bool WpfUiHooked;
    private static bool VioletaHooked;
    private static bool BetterGiHooked;
    private static bool ComboHooked;
    private static bool WebViewHooked;

    private static Type DpType;
    private static Type PmType;
    private static Type CoerceDelType;
    private static MethodInfo GetMetaMethod;
    private static PropertyInfo DefaultMetaProp;
    private static FieldInfo CoerceField;
    private static PropertyInfo VioletaCultureProp;

    private static Type ContentControlType;
    private static Type ComboBoxItemType;
    private static Type ListBoxItemType;
    private static Type ListViewItemType;
    private static Type TreeViewItemType;
    private static Type ContentPresenterType;
    private static Type PageType;
    private static Type UserControlType;
    private static Type WindowType;
    private static Type TextBoxBaseType;
    private static Type ScrollViewerType;
    private static Type FrameType;

    private static Type ComboBoxType;
    private static Type HeaderedContentControlType;
    private static Type HeaderedItemsControlType;
    private static Type TextBlockType;
    private static object ContentDp;
    private static object HeaderContentDp;
    private static object HeaderItemsDp;
    private static object TextBlockTextDp;
    private static object WindowTitleDp;
    private static Type CascadeSelectorType;
    private static MethodInfo InvalidatePropMethod;
    private static PropertyInfo DispatcherProp;
    private static MethodInfo BeginInvokeMethod;
    private static Type ActionType = typeof(Action);
    private static object TranslatedItemTemplate;

    private static Type WebView2Type;
    private static Type VisualTreeHelperType;
    private static MethodInfo GetParentMethod;

    public static void Initialize()
    {
        try
        {
            GeminiThoughtProxy.Start();
            AutoTranslator.Start();
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            TryHookLoadedAssemblies();
        }
        catch
        {
        }
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        try
        {
            string name = args.LoadedAssembly.GetName().Name;
            if (name == "PresentationFramework" ||
                name == "PresentationCore" ||
                name == "WindowsBase" ||
                name == "Wpf.Ui" ||
                name == "Wpf.Ui.Violeta" ||
                name == "Wpf.Ui.Tray" ||
                name == "BetterGI" ||
                name == "Microsoft.Web.WebView2.Wpf" ||
                name == "Microsoft.Web.WebView2.Core" ||
                name == "Serilog.Sinks.RichTextBoxEx.Wpf")
            {
                TryHookLoadedAssemblies();
            }
        }
        catch
        {
        }
    }

    private static void EnsureDictLoaded()
    {
        if (DictLoaded) return;
        lock (SyncRoot)
        {
            if (DictLoaded) return;
            Dict = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string enPath = ResolveUserI18nFile("en.json");
                if (!string.IsNullOrEmpty(enPath) && File.Exists(enPath))
                {
                    string json = File.ReadAllText(enPath, Encoding.UTF8);
                    ParseFlatJson(json, Dict);
                }
            }
            catch
            {
            }

            if (!Dict.ContainsKey("群友分享"))
                Dict["群友分享"] = "Community Shared";
            if (!Dict.ContainsKey("请选择..."))
                Dict["请选择..."] = "Please select...";
            if (!Dict.ContainsKey("请选择"))
                Dict["请选择"] = "Please select";
            if (!Dict.ContainsKey("清空选择"))
                Dict["清空选择"] = "Clear selection";

            DictLoaded = true;
        }
    }

    internal static string ResolveUserI18nFile(string fileName)
    {
        string hookDir = null;
        try
        {
            string loc = typeof(StartupHook).Assembly.Location;
            if (!string.IsNullOrEmpty(loc))
                hookDir = Path.GetDirectoryName(loc);
        }
        catch
        {
        }

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(hookDir))
            candidates.Add(Path.Combine(hookDir, "User", "I18n", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "User", "I18n", fileName));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "User", "I18n", fileName));

        foreach (string path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        foreach (string path in candidates)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return path;
            }
            catch
            {
            }
        }
        return candidates.Count > 0 ? candidates[0] : null;
    }

    internal static void ParseFlatJson(string json, Dictionary<string, string> target)
    {
        int i = 0;
        int n = json.Length;
        while (i < n)
        {
            while (i < n && json[i] != '"') i++;
            if (i >= n) return;
            string key = ReadJsonString(json, ref i);

            while (i < n && json[i] != ':') i++;
            if (i >= n) return;
            i++;

            while (i < n && json[i] != '"' && json[i] != ',' && json[i] != '}') i++;
            if (i < n && json[i] == '"')
            {
                string val = ReadJsonString(json, ref i);
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                {
                    target[key] = val;
                }
            }
        }
    }

    internal static string ReadJsonString(string s, ref int i)
    {
        i++; // skip opening quote
        var sb = new StringBuilder();
        int n = s.Length;
        while (i < n)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && i < n)
            {
                char esc = s[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= n)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                            }
                            i += 4;
                        }
                        break;
                    default:
                        sb.Append(esc);
                        break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    internal static string EscapeJsonString(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static void TryHookLoadedAssemblies()
    {
        lock (SyncRoot)
        {
            try
            {
                Assembly winBase = null;
                Assembly presCore = null;
                Assembly presFw = null;
                Assembly wpfUi = null;
                Assembly violeta = null;
                Assembly betterGi = null;
                Assembly webView2Wpf = null;

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name;
                    if (name == "WindowsBase") winBase = asm;
                    else if (name == "PresentationCore") presCore = asm;
                    else if (name == "PresentationFramework") presFw = asm;
                    else if (name == "Wpf.Ui") wpfUi = asm;
                    else if (name == "Wpf.Ui.Violeta") violeta = asm;
                    else if (name == "BetterGI") betterGi = asm;
                    else if (name == "Microsoft.Web.WebView2.Wpf") webView2Wpf = asm;
                }

                if (winBase != null && DpType == null)
                {
                    DpType = winBase.GetType("System.Windows.DependencyProperty");
                    PmType = winBase.GetType("System.Windows.PropertyMetadata");
                    CoerceDelType = winBase.GetType("System.Windows.CoerceValueCallback");
                    Type doType = winBase.GetType("System.Windows.DependencyObject");
                    Type dispObjType = winBase.GetType("System.Windows.Threading.DispatcherObject");
                    Type dispType = winBase.GetType("System.Windows.Threading.Dispatcher");
                    if (DpType != null && PmType != null)
                    {
                        GetMetaMethod = DpType.GetMethod("GetMetadata", new Type[] { typeof(Type) });
                        DefaultMetaProp = DpType.GetProperty("DefaultMetadata", BindingFlags.Public | BindingFlags.Instance);
                        CoerceField = PmType.GetField("_coerceValueCallback", BindingFlags.NonPublic | BindingFlags.Instance);
                    }
                    if (doType != null)
                        InvalidatePropMethod = doType.GetMethod("InvalidateProperty", new Type[] { DpType });
                    if (dispObjType != null)
                        DispatcherProp = dispObjType.GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance);
                    if (dispType != null)
                        BeginInvokeMethod = dispType.GetMethod("BeginInvoke", new Type[] { typeof(Delegate), typeof(object[]) });
                }

                if (DpType == null || CoerceField == null) return;

                if (presCore != null && VisualTreeHelperType == null)
                {
                    VisualTreeHelperType = presCore.GetType("System.Windows.Media.VisualTreeHelper");
                    if (VisualTreeHelperType != null && winBase != null)
                    {
                        Type doType = winBase.GetType("System.Windows.DependencyObject");
                        if (doType != null)
                            GetParentMethod = VisualTreeHelperType.GetMethod("GetParent", new Type[] { doType });
                    }
                }

                if (presFw != null && !WpfBaseHooked)
                {
                    ContentControlType = presFw.GetType("System.Windows.Controls.ContentControl");
                    ComboBoxItemType = presFw.GetType("System.Windows.Controls.ComboBoxItem");
                    ListBoxItemType = presFw.GetType("System.Windows.Controls.ListBoxItem");
                    ListViewItemType = presFw.GetType("System.Windows.Controls.ListViewItem");
                    TreeViewItemType = presFw.GetType("System.Windows.Controls.TreeViewItem");
                    ContentPresenterType = presFw.GetType("System.Windows.Controls.ContentPresenter");
                    PageType = presFw.GetType("System.Windows.Controls.Page");
                    UserControlType = presFw.GetType("System.Windows.Controls.UserControl");
                    WindowType = presFw.GetType("System.Windows.Window");
                    TextBoxBaseType = presFw.GetType("System.Windows.Controls.Primitives.TextBoxBase");
                    ScrollViewerType = presFw.GetType("System.Windows.Controls.ScrollViewer");
                    FrameType = presFw.GetType("System.Windows.Controls.Frame");
                    ComboBoxType = presFw.GetType("System.Windows.Controls.ComboBox");
                    HeaderedContentControlType = presFw.GetType("System.Windows.Controls.HeaderedContentControl");
                    HeaderedItemsControlType = presFw.GetType("System.Windows.Controls.HeaderedItemsControl");
                    TextBlockType = presFw.GetType("System.Windows.Controls.TextBlock");

                    TextBlockTextDp = HookProperty(TextBlockType, "TextProperty");
                    HookProperty(presFw.GetType("System.Windows.Controls.AccessText"), "TextProperty");
                    HookProperty(presFw.GetType("System.Windows.Documents.Run"), "TextProperty");
                    WindowTitleDp = HookProperty(WindowType, "TitleProperty");
                    HookProperty(presFw.GetType("System.Windows.FrameworkElement"), "ToolTipProperty");
                    HookProperty(presFw.GetType("System.Windows.Controls.ToolTipService"), "ToolTipProperty");
                    HeaderContentDp = HookProperty(HeaderedContentControlType, "HeaderProperty");
                    HeaderItemsDp = HookProperty(HeaderedItemsControlType, "HeaderProperty");
                    HookProperty(presFw.GetType("System.Windows.Controls.GridViewColumn"), "HeaderProperty");
                    HookProperty(presFw.GetType("System.Windows.Controls.DataGridColumn"), "HeaderProperty");
                    ContentDp = HookProperty(ContentControlType, "ContentProperty");
                    HookDerivedMetadatas(presFw);
                    WpfBaseHooked = true;
                }

                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return;

                if (presFw != null && presCore != null && !ComboHooked)
                {
                    HookComboAndItemTemplates(presFw, presCore);
                    ComboHooked = true;
                }

                if (wpfUi != null && !WpfUiHooked)
                {
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.TextBlock"), "TextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.TitleBar"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.TitleBar"), "HeaderProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.InfoBar"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.InfoBar"), "MessageProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.Snackbar"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.MessageBox"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.MessageBox"), "PrimaryButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.MessageBox"), "SecondaryButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.MessageBox"), "CloseButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ContentDialog"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ContentDialog"), "PrimaryButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ContentDialog"), "SecondaryButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ContentDialog"), "CloseButtonTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.TextBox"), "PlaceholderTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.AutoSuggestBox"), "PlaceholderTextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.CardControl"), "HeaderProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.CardColor"), "TitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.CardColor"), "SubtitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.NavigationView"), "HeaderProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.NavigationView"), "PaneTitleProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.NavigationViewItemHeader"), "TextProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ToggleSwitch"), "OffContentProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.ToggleSwitch"), "OnContentProperty");
                    HookProperty(wpfUi.GetType("Wpf.Ui.Controls.TimePicker"), "HeaderProperty");
                    HookDerivedMetadatas(wpfUi);
                    WpfUiHooked = true;
                }

                if (violeta != null && !VioletaHooked)
                {
                    Type i18nType = violeta.GetType("Wpf.Ui.Violeta.Appearance.I18N");
                    if (i18nType != null)
                    {
                        VioletaCultureProp = i18nType.GetProperty("Culture", BindingFlags.Public | BindingFlags.Static);
                    }
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.CascadingComboBox"), "PlaceholderTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.CascadingComboBox"), "ClearButtonToolTipProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ToastControl"), "MessageProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.TitleBar"), "TitleProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentWindowControl"), "TitleProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentWindowDialogControl"), "PrimaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentWindowDialogControl"), "SecondaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentWindowDialogControl"), "CloseButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentDialog"), "TitleProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentDialog"), "PrimaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentDialog"), "SecondaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ContentDialog"), "CloseButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MessageBoxDialog"), "OKButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MessageBoxDialog"), "YesButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MessageBoxDialog"), "NoButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MessageBoxDialog"), "CancelButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.TreeComboBox"), "PlaceholderTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.TreeComboBox"), "ClearButtonToolTipProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ToggleSwitch"), "HeaderProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ToggleSwitch"), "OffContentProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.ToggleSwitch"), "OnContentProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MultiComboBox"), "PlaceholderTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MultiComboBox"), "SelectAllTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MultiComboBox"), "ShowAllSelectedTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.MultiComboBox"), "AllSelectedTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.Growl"), "MessageProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PendingBoxDialog"), "MessageProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PopConfirmButton"), "TitleProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PopConfirmButton"), "MessageProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PopConfirmButton"), "PrimaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PopConfirmButton"), "SecondaryButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.PopConfirmButton"), "CloseButtonTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.LabeledContentControl"), "LabelProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.FormItem"), "LabelProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Violeta.Controls.Transfer"), "EmptyTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Controls.ControlHelper"), "HeaderProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Controls.ControlHelper"), "PlaceholderTextProperty");
                    HookProperty(violeta.GetType("Wpf.Ui.Controls.ControlHelper"), "DescriptionProperty");
                    HookDerivedMetadatas(violeta);
                    VioletaHooked = true;
                }

                if (betterGi != null && !BetterGiHooked)
                {
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Behavior.DomainCascadingComboBoxBehavior"), "LastCommittedTextProperty");
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Controls.TwoStateButton"), "DisableContentProperty");
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Controls.TwoStateButton"), "EnableContentProperty");
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Controls.MultiSelectComboBox"), "PlaceholderTextProperty");
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Controls.MultiSelectComboBox"), "SearchPlaceholderTextProperty");
                    HookProperty(betterGi.GetType("BetterGenshinImpact.View.Controls.MultiSelectComboBox"), "SelectionTextProperty");

                    CascadeSelectorType = betterGi.GetType("BetterGenshinImpact.View.Controls.CascadeSelector");
                    if (CascadeSelectorType != null && presFw != null && presCore != null)
                    {
                        RegisterLoadedHandler(presFw, presCore, CascadeSelectorType, "OnCascadeSelectorLoaded");
                    }
                    HookDerivedMetadatas(betterGi);
                    BetterGiHooked = true;
                }

                if (webView2Wpf != null && presFw != null && presCore != null && !WebViewHooked)
                {
                    WebView2Type = webView2Wpf.GetType("Microsoft.Web.WebView2.Wpf.WebView2");
                    if (WebView2Type != null)
                    {
                        RegisterLoadedHandler(presFw, presCore, WebView2Type, "OnWebView2Loaded");
                        WebViewHooked = true;
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static void HookComboAndItemTemplates(Assembly presFw, Assembly presCore)
    {
        try
        {
            if (ComboBoxType != null)
            {
                RegisterLoadedHandler(presFw, presCore, ComboBoxType, "OnComboBoxLoaded");
            }
            if (ComboBoxItemType != null)
            {
                RegisterLoadedHandler(presFw, presCore, ComboBoxItemType, "OnComboBoxItemLoaded");
            }
        }
        catch
        {
        }
    }

    private static void RegisterLoadedHandler(Assembly presFw, Assembly presCore, Type targetType, string methodName)
    {
        try
        {
            Type emType = presCore.GetType("System.Windows.EventManager");
            Type reType = presCore.GetType("System.Windows.RoutedEvent");
            Type feType = presFw.GetType("System.Windows.FrameworkElement");
            Type rehType = presCore.GetType("System.Windows.RoutedEventHandler");
            Type reaType = presCore.GetType("System.Windows.RoutedEventArgs");
            if (emType == null || reType == null || feType == null || rehType == null || reaType == null) return;

            FieldInfo fi = feType.GetField("LoadedEvent", BindingFlags.Public | BindingFlags.Static);
            if (fi == null) return;
            object loadedEvent = fi.GetValue(null);
            if (loadedEvent == null) return;

            MethodInfo regMethod = emType.GetMethod("RegisterClassHandler", new Type[] { typeof(Type), reType, typeof(Delegate), typeof(bool) });
            if (regMethod == null) return;

            MethodInfo targetMethod = typeof(StartupHook).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (targetMethod == null) return;

            ParameterExpression pSender = Expression.Parameter(typeof(object), "sender");
            ParameterExpression pArgs = Expression.Parameter(reaType, "e");
            MethodCallExpression call = Expression.Call(targetMethod, pSender, Expression.Convert(pArgs, typeof(object)));
            Delegate handler = Expression.Lambda(rehType, call, pSender, pArgs).Compile();

            regMethod.Invoke(null, new object[] { targetType, loadedEvent, handler, true });
        }
        catch
        {
        }
    }

    private static object GetTranslatedItemTemplate()
    {
        if (TranslatedItemTemplate != null) return TranslatedItemTemplate;
        try
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "PresentationFramework")
                {
                    Type xrType = asm.GetType("System.Windows.Markup.XamlReader");
                    if (xrType != null)
                    {
                        MethodInfo parseMethod = xrType.GetMethod("Parse", new Type[] { typeof(string) });
                        if (parseMethod != null)
                        {
                            string xaml = "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock Text=\"{Binding}\" TextTrimming=\"CharacterEllipsis\"/></DataTemplate>";
                            TranslatedItemTemplate = parseMethod.Invoke(null, new object[] { xaml });
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
        }
        return TranslatedItemTemplate;
    }

    internal static void OnComboBoxLoaded(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            ApplyComboBoxTemplateIfNeeded(sender);

            object dummy;
            if (!ConfiguredCombos.TryGetValue(sender, out dummy))
            {
                ConfiguredCombos.Add(sender, new object());
                EventInfo ddEvent = sender.GetType().GetEvent("DropDownOpened", BindingFlags.Public | BindingFlags.Instance);
                if (ddEvent != null)
                {
                    MethodInfo mi = typeof(StartupHook).GetMethod("OnComboBoxDropDownOpened", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    Delegate del = CreateEventHandlerDelegate(ddEvent.EventHandlerType, mi);
                    if (del != null) ddEvent.AddEventHandler(sender, del);
                }
            }
        }
        catch
        {
        }
    }

    internal static void OnComboBoxDropDownOpened(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            ApplyComboBoxTemplateIfNeeded(sender);
        }
        catch
        {
        }
    }

    private static void ApplyComboBoxTemplateIfNeeded(object cb)
    {
        Type t = cb.GetType();
        PropertyInfo itemTplProp = t.GetProperty("ItemTemplate", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo dmpProp = t.GetProperty("DisplayMemberPath", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo itsProp = t.GetProperty("ItemTemplateSelector", BindingFlags.Public | BindingFlags.Instance);
        if (itemTplProp == null) return;

        if (itemTplProp.GetValue(cb, null) != null) return;
        if (itsProp != null && itsProp.GetValue(cb, null) != null) return;
        if (dmpProp != null && !string.IsNullOrEmpty(dmpProp.GetValue(cb, null) as string)) return;

        bool isStringList = false;
        PropertyInfo itemsSourceProp = t.GetProperty("ItemsSource", BindingFlags.Public | BindingFlags.Instance);
        object itemsSource = itemsSourceProp != null ? itemsSourceProp.GetValue(cb, null) : null;
        if (itemsSource is IEnumerable<string>)
        {
            isStringList = true;
        }
        else
        {
            PropertyInfo itemsProp = t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
            IEnumerable items = itemsProp != null ? itemsProp.GetValue(cb, null) as IEnumerable : null;
            if (items != null)
            {
                foreach (object item in items)
                {
                    if (item != null)
                    {
                        isStringList = item is string;
                        break;
                    }
                }
            }
        }

        if (isStringList)
        {
            object tpl = GetTranslatedItemTemplate();
            if (tpl != null)
                itemTplProp.SetValue(cb, tpl, null);
        }
    }

    internal static void OnComboBoxItemLoaded(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            Type t = sender.GetType();
            PropertyInfo contentProp = t.GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo ctProp = t.GetProperty("ContentTemplate", BindingFlags.Public | BindingFlags.Instance);
            if (contentProp == null || ctProp == null) return;
            if (ctProp.GetValue(sender, null) != null) return;
            if (contentProp.GetValue(sender, null) is string)
            {
                object tpl = GetTranslatedItemTemplate();
                if (tpl != null)
                    ctProp.SetValue(sender, tpl, null);
            }
        }
        catch
        {
        }
    }

    internal static void OnCascadeSelectorLoaded(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            object tpl = GetTranslatedItemTemplate();
            if (tpl == null) return;

            Type t = sender.GetType();
            string[] lvFields = new string[] { "FirstLevelListView", "SecondLevelListView" };
            for (int i = 0; i < lvFields.Length; i++)
            {
                FieldInfo fi = t.GetField(lvFields[i], BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                object lv = fi != null ? fi.GetValue(sender) : null;
                if (lv != null)
                {
                    PropertyInfo itemTplProp = lv.GetType().GetProperty("ItemTemplate", BindingFlags.Public | BindingFlags.Instance);
                    if (itemTplProp != null && itemTplProp.GetValue(lv, null) == null)
                    {
                        itemTplProp.SetValue(lv, tpl, null);
                    }
                }
            }
        }
        catch
        {
        }
    }

    internal static void OnWebView2Loaded(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            if (!IsInsideWebpagePanel(sender)) return;

            Type t = sender.GetType();
            PropertyInfo coreProp = t.GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance);
            object core = coreProp != null ? coreProp.GetValue(sender, null) : null;
            if (core != null)
            {
                HookCoreWebView2(core);
            }
            else
            {
                EventInfo initEvent = t.GetEvent("CoreWebView2InitializationCompleted", BindingFlags.Public | BindingFlags.Instance);
                if (initEvent != null)
                {
                    MethodInfo mi = typeof(StartupHook).GetMethod("OnWebView2CoreInitCompleted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    Delegate del = CreateEventHandlerDelegate(initEvent.EventHandlerType, mi);
                    if (del != null)
                    {
                        initEvent.RemoveEventHandler(sender, del);
                        initEvent.AddEventHandler(sender, del);
                    }
                }
            }
        }
        catch
        {
        }
    }

    internal static void OnWebView2CoreInitCompleted(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            PropertyInfo coreProp = sender.GetType().GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance);
            object core = coreProp != null ? coreProp.GetValue(sender, null) : null;
            if (core != null)
            {
                HookCoreWebView2(core);
            }
        }
        catch
        {
        }
    }

    private static bool IsInsideWebpagePanel(object depObj)
    {
        if (GetParentMethod == null) return true;
        try
        {
            object cur = depObj;
            for (int depth = 0; depth < 14 && cur != null; depth++)
            {
                string fullName = cur.GetType().FullName;
                if (fullName == "BetterGenshinImpact.View.Controls.Webview.WebpagePanel")
                    return true;
                if (fullName == "BetterGenshinImpact.View.HtmlMaskWindow" ||
                    fullName == "BetterGenshinImpact.View.Windows.CloudWebHostWindow")
                    return false;
                cur = GetParentMethod.Invoke(null, new object[] { cur });
            }
        }
        catch
        {
        }
        return false;
    }

    private static void HookCoreWebView2(object coreWebView2)
    {
        if (coreWebView2 == null) return;
        int hash = RuntimeHelpers.GetHashCode(coreWebView2);
        lock (SyncRoot)
        {
            if (HookedCoreWebViews.Contains(hash)) return;
            HookedCoreWebViews.Add(hash);
        }

        try
        {
            Type coreType = coreWebView2.GetType();
            EventInfo msgEvent = coreType.GetEvent("WebMessageReceived", BindingFlags.Public | BindingFlags.Instance);
            if (msgEvent != null)
            {
                MethodInfo mi = typeof(StartupHook).GetMethod("OnCoreWebMessageReceived", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Delegate del = CreateEventHandlerDelegate(msgEvent.EventHandlerType, mi);
                if (del != null) msgEvent.AddEventHandler(coreWebView2, del);
            }

            EventInfo navEvent = coreType.GetEvent("NavigationCompleted", BindingFlags.Public | BindingFlags.Instance);
            if (navEvent != null)
            {
                MethodInfo mi = typeof(StartupHook).GetMethod("OnCoreNavigationCompleted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Delegate del = CreateEventHandlerDelegate(navEvent.EventHandlerType, mi);
                if (del != null) navEvent.AddEventHandler(coreWebView2, del);
            }

            MethodInfo addScriptMethod = coreType.GetMethod("AddScriptToExecuteOnDocumentCreatedAsync", new Type[] { typeof(string) });
            if (addScriptMethod != null)
            {
                addScriptMethod.Invoke(coreWebView2, new object[] { WebRepoTranslator.GetInjectedJs() });
            }

            MethodInfo execScriptMethod = coreType.GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
            if (execScriptMethod != null)
            {
                execScriptMethod.Invoke(coreWebView2, new object[] { WebRepoTranslator.GetInjectedJs() });
            }
        }
        catch
        {
        }
    }

    internal static void OnCoreNavigationCompleted(object sender, object args)
    {
        if (sender == null || !IsEnglishMode()) return;
        try
        {
            MethodInfo execScriptMethod = sender.GetType().GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
            if (execScriptMethod != null)
            {
                execScriptMethod.Invoke(sender, new object[] { WebRepoTranslator.GetInjectedJs() });
            }
        }
        catch
        {
        }
    }

    internal static void OnCoreWebMessageReceived(object sender, object args)
    {
        if (sender == null || args == null || !IsEnglishMode()) return;
        try
        {
            MethodInfo tryGetStr = args.GetType().GetMethod("TryGetWebMessageAsString", Type.EmptyTypes);
            string msg = tryGetStr != null ? tryGetStr.Invoke(args, null) as string : null;
            if (string.IsNullOrEmpty(msg)) return;
            if (msg.IndexOf("__bgiEnTr", StringComparison.Ordinal) < 0) return;

            WebRepoTranslator.HandleMessage(sender, msg);
        }
        catch
        {
        }
    }

    private static Delegate CreateEventHandlerDelegate(Type delegateType, MethodInfo targetMethod)
    {
        if (delegateType == null || targetMethod == null) return null;
        MethodInfo invoke = delegateType.GetMethod("Invoke");
        ParameterInfo[] ps = invoke.GetParameters();
        if (ps.Length != 2) return null;

        ParameterExpression p0 = Expression.Parameter(ps[0].ParameterType, "s");
        ParameterExpression p1 = Expression.Parameter(ps[1].ParameterType, "e");
        MethodCallExpression call = Expression.Call(
            targetMethod,
            Expression.Convert(p0, typeof(object)),
            Expression.Convert(p1, typeof(object)));
        return Expression.Lambda(delegateType, call, p0, p1).Compile();
    }

    private static object HookProperty(Type ownerType, string fieldName)
    {
        if (ownerType == null) return null;
        try
        {
            try { RuntimeHelpers.RunClassConstructor(ownerType.TypeHandle); }
            catch { }
            FieldInfo fi = ownerType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (fi == null) return null;
            object dp = fi.GetValue(null);
            if (dp == null) return null;

            object meta = GetMetaMethod.Invoke(dp, new object[] { ownerType });
            object defMeta = DefaultMetaProp.GetValue(dp, null);
            AttachCoerce(meta, dp);
            AttachCoerce(defMeta, dp);
            return dp;
        }
        catch
        {
            return null;
        }
    }

    private static void HookDerivedMetadatas(Assembly asm)
    {
        if (asm == null || GetMetaMethod == null) return;
        try
        {
            Type[] types = null;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            if (types == null) return;

            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || t.IsGenericTypeDefinition || t.IsAbstract) continue;
                try
                {
                    bool isContent = ContentDp != null && ContentControlType != null && ContentControlType.IsAssignableFrom(t);
                    bool isHeaderContent = HeaderContentDp != null && HeaderedContentControlType != null && HeaderedContentControlType.IsAssignableFrom(t);
                    bool isHeaderItems = HeaderItemsDp != null && HeaderedItemsControlType != null && HeaderedItemsControlType.IsAssignableFrom(t);
                    bool isTextBlock = TextBlockTextDp != null && TextBlockType != null && TextBlockType.IsAssignableFrom(t);
                    bool isWindow = WindowTitleDp != null && WindowType != null && WindowType.IsAssignableFrom(t);
                    if (!isContent && !isHeaderContent && !isHeaderItems && !isTextBlock && !isWindow) continue;

                    try { RuntimeHelpers.RunClassConstructor(t.TypeHandle); }
                    catch { }

                    if (isContent)
                        AttachCoerce(GetMetaMethod.Invoke(ContentDp, new object[] { t }), ContentDp);
                    if (isHeaderContent)
                        AttachCoerce(GetMetaMethod.Invoke(HeaderContentDp, new object[] { t }), HeaderContentDp);
                    if (isHeaderItems)
                        AttachCoerce(GetMetaMethod.Invoke(HeaderItemsDp, new object[] { t }), HeaderItemsDp);
                    if (isTextBlock)
                        AttachCoerce(GetMetaMethod.Invoke(TextBlockTextDp, new object[] { t }), TextBlockTextDp);
                    if (isWindow)
                        AttachCoerce(GetMetaMethod.Invoke(WindowTitleDp, new object[] { t }), WindowTitleDp);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void AttachCoerce(object meta, object dp)
    {
        if (meta == null) return;
        lock (SyncRoot)
        {
            if (HookedMetadatas.Contains(meta)) return;
            HookedMetadatas.Add(meta);

            Delegate existing = CoerceField.GetValue(meta) as Delegate;
            CoerceWrapper wrapper = new CoerceWrapper(existing, dp);
            Delegate del = Delegate.CreateDelegate(
                CoerceDelType,
                wrapper,
                typeof(CoerceWrapper).GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance));
            CoerceField.SetValue(meta, del);
        }
    }

    internal static bool IsSafeContentControl(object d)
    {
        if (d == null) return false;
        if (ComboBoxItemType != null && ComboBoxItemType.IsInstanceOfType(d)) return false;
        if (ListBoxItemType != null && ListBoxItemType.IsInstanceOfType(d)) return false;
        if (ListViewItemType != null && ListViewItemType.IsInstanceOfType(d)) return false;
        if (TreeViewItemType != null && TreeViewItemType.IsInstanceOfType(d)) return false;
        if (ContentPresenterType != null && ContentPresenterType.IsInstanceOfType(d)) return false;
        if (PageType != null && PageType.IsInstanceOfType(d)) return false;
        if (UserControlType != null && UserControlType.IsInstanceOfType(d)) return false;
        if (WindowType != null && WindowType.IsInstanceOfType(d)) return false;
        if (TextBoxBaseType != null && TextBoxBaseType.IsInstanceOfType(d)) return false;
        if (ScrollViewerType != null && ScrollViewerType.IsInstanceOfType(d)) return false;
        if (FrameType != null && FrameType.IsInstanceOfType(d)) return false;
        if (WebView2Type != null && WebView2Type.IsInstanceOfType(d)) return false;
        return true;
    }

    internal static bool ContainsCjk(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) return true;
        }
        return false;
    }

    internal static bool IsEnglishMode()
    {
        CultureInfo ui = CultureInfo.CurrentUICulture;
        if (ui != null && ui.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static void SyncVioletaCultureIfNeeded()
    {
        try
        {
            if (VioletaCultureProp != null)
            {
                CultureInfo ci = VioletaCultureProp.GetValue(null, null) as CultureInfo;
                if (ci != null && ci.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    VioletaCultureProp.SetValue(null, new CultureInfo("en"), null);
                }
            }
        }
        catch
        {
        }
    }

    internal static string TranslateString(string s)
    {
        return TranslateString(s, null, null, true);
    }

    internal static string TranslateString(string s, object depObj, object dp, bool allowAutoQueue)
    {
        try
        {
            if (!WpfUiHooked || !VioletaHooked || !BetterGiHooked || !ComboHooked || !WebViewHooked)
            {
                TryHookLoadedAssemblies();
            }

            if (string.IsNullOrEmpty(s) || !ContainsCjk(s)) return s;
            if (!IsEnglishMode()) return s;

            SyncVioletaCultureIfNeeded();
            EnsureDictLoaded();

            // 1. Exact & trimmed dictionary lookup
            string mapped = LookupDict(s);
            if (mapped != null) return mapped;

            string trimmed = s.Trim();
            if (trimmed.Length > 0 && trimmed.Length != s.Length)
            {
                string trimMapped = LookupDict(trimmed);
                if (trimMapped != null)
                {
                    int idx = s.IndexOf(trimmed, StringComparison.Ordinal);
                    return s.Substring(0, idx) + trimMapped + s.Substring(idx + trimmed.Length);
                }
            }

            // 2. Persistent auto-translation cache lookup
            string cached = AutoTranslator.TryGetCached(s);
            if (cached != null) return cached;
            if (trimmed.Length > 0 && trimmed.Length != s.Length)
            {
                string trimCached = AutoTranslator.TryGetCached(trimmed);
                if (trimCached != null)
                {
                    int idx = s.IndexOf(trimmed, StringComparison.Ordinal);
                    return s.Substring(0, idx) + trimCached + s.Substring(idx + trimmed.Length);
                }
            }

            // 3. Domain-specific structural / pattern translations
            string childSessionTranslated = TranslateChildSessionString(s);
            if (childSessionTranslated != null) return childSessionTranslated;

            string logTranslated = TranslateLogPattern(s);
            if (logTranslated != null)
            {
                if (ContainsCjk(logTranslated))
                    logTranslated = TranslateGlossary(logTranslated);
                return logTranslated;
            }

            string uiPattern = TranslateUiPattern(s);
            if (uiPattern != null)
            {
                if (ContainsCjk(uiPattern))
                    uiPattern = TranslateGlossary(uiPattern);
                return uiPattern;
            }

            string scriptPattern = TranslateScriptPattern(s);
            if (scriptPattern != null)
            {
                if (ContainsCjk(scriptPattern))
                    scriptPattern = TranslateGlossary(scriptPattern);
                return scriptPattern;
            }

            // 4. Multi-line decomposition (especially for script settings.json labels)
            if (s.IndexOf('\n') >= 0)
            {
                string[] lines = s.Split(new char[] { '\n' });
                bool anyChanged = false;
                bool anyMissing = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    bool hadCr = lines[i].EndsWith("\r", StringComparison.Ordinal);
                    if (ContainsCjk(line))
                    {
                        string trLine = TranslateString(line, depObj, dp, allowAutoQueue);
                        if (trLine != line) anyChanged = true;
                        if (ContainsCjk(trLine)) anyMissing = true;
                        lines[i] = hadCr ? (trLine + "\r") : trLine;
                    }
                }
                if (anyChanged && !anyMissing)
                    return string.Join("\n", lines);
                if (anyChanged)
                    return string.Join("\n", lines);
            }

            // 5. Separators (|, /, \)
            int pipeIdx = s.IndexOf(" | ", StringComparison.Ordinal);
            if (pipeIdx > 0)
            {
                string left = s.Substring(0, pipeIdx).Trim();
                string right = s.Substring(pipeIdx + 3).Trim();
                string leftTr = TranslateToken(left);
                string[] parts = right.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i] = TranslateToken(parts[i]);
                }
                return leftTr + " | " + string.Join(" / ", parts);
            }

            int slashIdx = s.IndexOf(" / ", StringComparison.Ordinal);
            if (slashIdx > 0)
            {
                string[] parts = s.Split(new string[] { " / " }, StringSplitOptions.None);
                bool anyChanged = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string partTrim = parts[i].Trim();
                    string tr = TranslateToken(partTrim);
                    if (tr != partTrim) anyChanged = true;
                    parts[i] = tr;
                }
                if (anyChanged) return string.Join(" / ", parts);
            }

            if (s.IndexOf('\\') > 0 && !s.Contains(":\\"))
            {
                string[] parts = s.Split('\\');
                bool anyChanged = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string tr = TranslateToken(parts[i]);
                    if (tr != parts[i]) anyChanged = true;
                    parts[i] = tr;
                }
                if (anyChanged) return string.Join("\\", parts);
            }

            // 6. Queue unseen CJK string for non-blocking background translation + live UI refresh
            if (allowAutoQueue && depObj != null && dp != null)
            {
                AutoTranslator.EnqueueWpf(trimmed.Length > 0 ? trimmed : s, depObj, dp);
            }

            // 7. Immediate offline glossary fallback so partial known terms still translate right away
            string gl = TranslateGlossary(s);
            if (gl != s) return gl;

            return s;
        }
        catch
        {
            return s;
        }
    }

    internal static string LookupDict(string key)
    {
        if (Dict == null || string.IsNullOrEmpty(key)) return null;
        string mapped;
        if (Dict.TryGetValue(key, out mapped) && !string.IsNullOrEmpty(mapped))
            return mapped;
        return null;
    }

    private static string TranslateToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return token;
        string mapped = LookupDict(token);
        if (mapped != null) return mapped;
        string cached = AutoTranslator.TryGetCached(token);
        if (cached != null) return cached;
        return TranslateGlossary(token);
    }

    private static string TranslateUiPattern(string s)
    {
        if (s.Contains("更好的原神"))
            return s.Replace("更好的原神", "Better Genshin Impact");

        if (s.StartsWith("当前测试引擎：", StringComparison.Ordinal))
            return "Current Benchmark Engine: " + TranslateToken(s.Substring("当前测试引擎：".Length).Trim());

        if (s.StartsWith("已选择：", StringComparison.Ordinal))
            return "Selected: " + TranslateToken(s.Substring("已选择：".Length).Trim());

        if (s.StartsWith("已选 ", StringComparison.Ordinal) && s.EndsWith(" 项", StringComparison.Ordinal))
            return "Selected " + s.Substring(3, s.Length - 5) + " item(s)";

        if (s.StartsWith("已选择 ", StringComparison.Ordinal) && s.Contains(" 个事件"))
        {
            return s.Replace("已选择 ", "Selected ")
                    .Replace(" 个事件；另有 ", " event(s); plus ")
                    .Replace(" 个事件", " event(s)")
                    .Replace(" 个未知事件代码，当前未显示；修改后会自动清理。未勾选任何事件时按“全部通知”处理", " unknown event code(s) (cleaned on change)")
                    .Replace(" 个未知事件代码，修改后会自动清理", " unknown event code(s) (cleaned on change)");
        }

        if (s.StartsWith("当前可用逻辑处理器数：", StringComparison.Ordinal))
            return "Available logical processors: " + s.Substring("当前可用逻辑处理器数：".Length);

        if (s.StartsWith("已发现 ", StringComparison.Ordinal) && s.EndsWith(" 个支持的设备", StringComparison.Ordinal))
            return "Found " + s.Substring(4, s.Length - 11) + " supported device(s)";

        if (s.StartsWith("第 ", StringComparison.Ordinal) && s.EndsWith(" 段", StringComparison.Ordinal))
            return "Segment " + s.Substring(2, s.Length - 4);

        if (s.EndsWith("（运行中）", StringComparison.Ordinal))
            return TranslateToken(s.Substring(0, s.Length - 5)) + " (Running)";

        if (s.EndsWith(" · 运行库信息", StringComparison.Ordinal))
            return TranslateToken(s.Substring(0, s.Length - 6)) + " · Runtime Info";

        if (s.EndsWith(" 已就绪，点击“应用并重启”即可使用。", StringComparison.Ordinal))
            return s.Substring(0, s.Length - 19) + " is ready. Click \"Apply & Restart\" to use it.";

        if (s.StartsWith("直接打开 (", StringComparison.Ordinal))
            return "Open Directly (" + s.Substring("直接打开 (".Length);

        if (s.StartsWith("脚本仓库已经 ", StringComparison.Ordinal))
        {
            return s.Replace("脚本仓库已经 ", "The script repository has not been updated for ")
                    .Replace(" 天未更新", " day(s).")
                    .Replace("温馨提示：", "Tip:")
                    .Replace("脚本内容跟随仓库版本，旧版仓库会订阅到旧版脚本。", "Script contents follow your local repository version; an outdated repo installs outdated scripts.")
                    .Replace("更新仓库后需要重新订阅脚本，以更新脚本内容。", "After updating the repository, re-subscribe to scripts to update them.")
                    .Replace("是否立即更新？", "Update now?");
        }

        if (s.StartsWith("这看起来不像是原神，确定要选择这个窗口吗？", StringComparison.Ordinal))
        {
            return s.Replace("这看起来不像是原神，确定要选择这个窗口吗？", "This doesn't look like Genshin Impact. Select this window anyway?")
                    .Replace("当前选择的窗口：", "Selected window: ");
        }

        if (s.StartsWith("发现新版本 ", StringComparison.Ordinal))
            return "New version found: " + s.Substring("发现新版本 ".Length);

        if (s.StartsWith("当前任务 <", StringComparison.Ordinal))
            return s.Replace("当前任务 <", "Current task <").Replace("> 已禁用，请先启用后再从此开始执行", "> is disabled; enable it before starting from here.");

        if (s.StartsWith("设置从 <", StringComparison.Ordinal))
            return s.Replace("设置从 <", "Set to start task list from <").Replace("> 开始执行任务列表", ">");

        if (s.StartsWith("确定要删除脚本 ", StringComparison.Ordinal))
            return "Delete script " + s.Substring("确定要删除脚本 ".Length).Replace(" 吗？", "?");

        if (s.StartsWith("确定要删除", StringComparison.Ordinal))
            return s.Replace("确定要删除", "Delete ").Replace("及其所有内容，无法恢复！", " and all its contents? This cannot be undone!").Replace(" 吗？", "?");

        if (s.StartsWith("确定删除实例「", StringComparison.Ordinal))
            return s.Replace("确定删除实例「", "Delete instance \"").Replace("」吗？\n该实例的登录态等 WebView 数据会一并删除，且无法恢复。", "\"?\nAll WebView login data for this instance will be permanently deleted.");

        if (s.StartsWith("确定要删除配置「", StringComparison.Ordinal))
            return s.Replace("确定要删除配置「", "Delete profile \"").Replace("」吗？", "\"?");

        if (s.StartsWith("配置组 ", StringComparison.Ordinal) && s.Contains(" 已经存在"))
            return s.Replace("配置组 ", "Script group ").Replace(" 已经存在，复制失败", " already exists (copy failed)").Replace(" 已经存在，重命名失败", " already exists (rename failed)").Replace(" 已经存在，请勿重复添加", " already exists").Replace(" 已经被删除", " has been deleted").Replace(" 保存失败！", " failed to save!").Replace(" 删除失败！", " failed to delete!");

        if (s.StartsWith("一条龙配置 ", StringComparison.Ordinal))
            return s.Replace("一条龙配置 ", "One-Dragon profile ").Replace(" 已经存在，请勿重复添加", " already exists");

        if (s.StartsWith("配置名称「", StringComparison.Ordinal))
            return s.Replace("配置名称「", "Profile name \"").Replace("」已存在，请使用其他名称", "\" already exists, please choose another name");

        if (s.StartsWith("云原神 · ", StringComparison.Ordinal))
            return "Cloud Genshin · " + s.Substring("云原神 · ".Length);

        if (s.StartsWith("云原神网页版「", StringComparison.Ordinal))
        {
            return s.Replace("云原神网页版「", "Cloud Genshin Web \"")
                    .Replace("」已打开 ", "\" opened ")
                    .Replace("，请在窗口中登录并进入游戏", ". Please sign in and enter the game in the window.");
        }

        if (s.StartsWith("已拉取 ", StringComparison.Ordinal) && s.Contains("个模型"))
            return s.Replace("已拉取 ", "Fetched ").Replace(" 个模型，可在“LLM模型名”下拉选择", " model(s); select in the \"LLM Model\" dropdown");

        if (s.StartsWith("已更新 ", StringComparison.Ordinal) || s.StartsWith("已自动更新 ", StringComparison.Ordinal))
        {
            return s.Replace("已自动更新 ", "Auto-updated ")
                    .Replace("已更新 ", "Updated ")
                    .Replace(" 个订阅脚本，", " subscribed script(s), ")
                    .Replace(" 个订阅脚本", " subscribed script(s)")
                    .Replace(" 个失败", " failed");
        }

        if (s.StartsWith("已移除 ", StringComparison.Ordinal) && s.Contains(" 下的所有关联配置"))
            return s.Replace("已移除 ", "Removed all linked configs under ").Replace(" 下的所有关联配置", "");

        if (s.StartsWith("检测到旧的 BetterGI 配置", StringComparison.Ordinal))
            return "Legacy BetterGI config detected. Migrate config and clean up old directory?";

        if (s.StartsWith("迁移配置成功", StringComparison.Ordinal))
            return "Configuration migrated successfully! BetterGI will exit now — please relaunch manually.";

        return null;
    }

    private static string TranslateScriptPattern(string s)
    {
        // Fishing spots: e.g. A001-蒙德-垂钓点-龙脊雪山寒天之钉西-普通
        if (s.Contains("-垂钓点-"))
        {
            return s.Replace("-垂钓点-", "-Fishing Spot-")
                    .Replace("-普通", "-Normal")
                    .Replace("-战斗", "-Combat");
        }

        // Script selection list: "FolderName - 中文脚本名"
        int dashIdx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
        {
            string left = s.Substring(0, dashIdx);
            string right = s.Substring(dashIdx + 3);
            string leftTr = ContainsCjk(left) ? TranslateToken(left) : left;
            string rightTr = ContainsCjk(right) ? TranslateToken(right) : right;
            if (leftTr != left || rightTr != right)
                return leftTr + " - " + rightTr;
        }

        // Section headers like "=== 蒙德 ==="
        if (s.StartsWith("=== ", StringComparison.Ordinal) && s.EndsWith(" ===", StringComparison.Ordinal))
        {
            string inner = s.Substring(4, s.Length - 8).Trim();
            return "=== " + TranslateToken(inner) + " ===";
        }

        return null;
    }

    private static string TranslateLogPattern(string s)
    {
        // High-frequency navigation/pathing/combat log lines
        Match m;

        if (s.StartsWith("粗略接近途经点，位置(", StringComparison.Ordinal))
            return "Approaching waypoint, pos (" + s.Substring("粗略接近途经点，位置(".Length);

        if (s.StartsWith("精确接近目标点，位置(", StringComparison.Ordinal))
            return "Precisely approaching target, pos (" + s.Substring("精确接近目标点，位置(".Length);

        if (s.StartsWith("朝向点，位置(", StringComparison.Ordinal))
            return "Facing point, pos (" + s.Substring("朝向点，位置(".Length);

        if (s.StartsWith("→ 开始执行地图追踪任务: ", StringComparison.Ordinal))
            return "-> Starting map pathing task: " + TranslateToken(s.Substring("→ 开始执行地图追踪任务: ".Length));

        if (s.StartsWith("→ 开始执行JS脚本: ", StringComparison.Ordinal))
            return "-> Starting JS script: " + TranslateToken(s.Substring("→ 开始执行JS脚本: ".Length));

        if (s.StartsWith("→ 脚本执行结束: ", StringComparison.Ordinal))
            return s.Replace("→ 脚本执行结束: ", "-> Script finished: ").Replace(", 耗时: ", ", elapsed: ").Replace("分", "m").Replace("秒", "s");

        if (s.Contains("元素战技，cd:"))
        {
            return s.Replace("点按元素战技，cd:", "Tap Elemental Skill, CD: ")
                    .Replace("长按元素战技，cd:", "Hold Elemental Skill, CD: ")
                    .Replace("元素战技，cd:", "Elemental Skill, CD: ")
                    .Replace(" 秒", "s")
                    .Replace("秒（兜底设置）", "s (fallback)");
        }

        if (s.StartsWith("[ONNX]", StringComparison.Ordinal))
        {
            return s.Replace("[ONNX]当前引擎 ", "[ONNX] Current engine ")
                    .Replace("，设备 ", ", device ")
                    .Replace("\"处理器\"", "\"CPU\"")
                    .Replace("，OCR 强制 CPU：", ", force CPU OCR: ")
                    .Replace("[ONNX]创建推理会话，模型: ", "[ONNX] Creating inference session, model: ")
                    .Replace("[ONNX]OCR CPU 线程策略，模型 ", "[ONNX] OCR CPU thread policy, model ")
                    .Replace("，算子内线程 ", ", intra-op ")
                    .Replace("，算子间线程 ", ", inter-op ")
                    .Replace("，并行 ", ", parallel ")
                    .Replace("，自旋 ", ", spin ")
                    .Replace("，使用 ONNX Runtime 自动调度", ", auto-scheduled by ONNX Runtime")
                    .Replace("[ONNX]OCR 验证完整推理已执行", "[ONNX] OCR verification full inference completed")
                    .Replace("[ONNX]古树检测完整推理已执行", "[ONNX] Petrified Tree detection full inference completed")
                    .Replace("[ONNX]主窗口推理验证失败，回退 CPU", "[ONNX] Main window inference verification failed, falling back to CPU")
                    .Replace("[ONNX]推理设备初始化失败，回退 CPU", "[ONNX] Inference device init failed, falling back to CPU");
        }

        if (s.StartsWith("实例 IPC v2 已启动：", StringComparison.Ordinal))
        {
            return s.Replace("实例 IPC v2 已启动：", "Instance IPC v2 started: ")
                    .Replace("，进程 ", ", PID ")
                    .Replace("，根管道 ", ", root pipe ");
        }

        if (s.StartsWith("游戏运行环境已绑定：", StringComparison.Ordinal))
            return s.Replace("游戏运行环境已绑定：", "Game runtime bound: ").Replace("，窗口句柄 ", ", HWND ");

        if (s.StartsWith("遮罩窗口已启动，", StringComparison.Ordinal))
        {
            return s.Replace("遮罩窗口已启动，游戏大小", "Overlay window started: game size ")
                    .Replace("，素材缩放", ", asset scale ")
                    .Replace("，DPI缩放", ", DPI scale ");
        }

        if (s.StartsWith("当前游戏语言", StringComparison.Ordinal) && s.Contains("不是简体中文"))
            return "Game language is not Simplified Chinese; some OCR features may not work properly.";

        if (s.StartsWith("检测到远程更新:", StringComparison.Ordinal))
            return s.Replace("检测到远程更新:", "Remote update detected:").Replace("本地 ", "local ").Replace(" -> 远程 ", " -> remote ");

        if (s.StartsWith("仓库目录结构重合度", StringComparison.Ordinal))
        {
            return s.Replace("仓库目录结构重合度(Overlap): ", "Repo directory overlap: ")
                    .Replace("(旧 ", "(old ")
                    .Replace(" 个目录, 新 ", " dirs, new ")
                    .Replace(" 个目录, 交集 ", " dirs, intersection ")
                    .Replace(" 个)", ")");
        }

        if (s.StartsWith("已标记repo.json中的更新节点并保存到: ", StringComparison.Ordinal))
            return "Marked updated nodes in repo.json and saved to: " + s.Substring("已标记repo.json中的更新节点并保存到: ".Length);

        if (s.StartsWith("自动更新订阅脚本完成:", StringComparison.Ordinal))
            return s.Replace("自动更新订阅脚本完成: 成功 ", "Auto-update subscribed scripts done: succeeded ").Replace(" 个, 失败 ", ", failed ").Replace(" 个", "");

        if (s.StartsWith("更新脚本成功: ", StringComparison.Ordinal))
            return "Updated script: " + s.Substring("更新脚本成功: ".Length);

        if (s.StartsWith("脚本 ", StringComparison.Ordinal))
        {
            if (s.EndsWith(" 没有需要保存的文件", StringComparison.Ordinal))
                return "Script " + s.Substring(3, s.Length - 12) + " has no files to preserve";
            if (s.EndsWith(" 状态为禁用，跳过执行", StringComparison.Ordinal))
                return "Script " + s.Substring(3, s.Length - 13) + " is disabled, skipping";
        }

        if (s.StartsWith("没有找到匹配的文件: ", StringComparison.Ordinal))
            return "No matching files found: " + s.Substring("没有找到匹配的文件: ".Length);

        if (s.StartsWith("创建了类型为 ", StringComparison.Ordinal))
            return s.Replace("创建了类型为 ", "Created OCR service of type ").Replace(" 的 OCR服务", "");

        if (s.StartsWith("识别到的队伍角色:", StringComparison.Ordinal))
            return "Detected party characters: " + TranslateGlossary(s.Substring("识别到的队伍角色:".Length));

        if (s.StartsWith("将传送至 ", StringComparison.Ordinal))
            return s.Replace("将传送至 ", "Teleporting to ").Replace(" 七天神像", " Statue of The Seven");

        if (s.StartsWith("开始传送：", StringComparison.Ordinal))
            return "Teleporting to: " + TranslateGlossary(s.Substring("开始传送：".Length));

        if (s.StartsWith("切换到区域：", StringComparison.Ordinal))
            return "Switching region to: " + TranslateGlossary(s.Substring("切换到区域：".Length));

        if (s.StartsWith("点击候选列表：", StringComparison.Ordinal))
            return "Clicking candidate: " + TranslateGlossary(s.Substring("点击候选列表：".Length));

        if (s.StartsWith("交互或拾取：", StringComparison.Ordinal))
            return "Interacting / picking up: " + TranslateGlossary(s.Substring("交互或拾取：".Length));

        if (s.StartsWith("配置组 ", StringComparison.Ordinal))
        {
            if (s.EndsWith(" 执行结束", StringComparison.Ordinal))
                return "Script group " + s.Substring(4, s.Length - 9) + " finished";
            if (s.Contains(" 加载完成，共"))
                return s.Replace("配置组 ", "Script group ").Replace(" 加载完成，共", " loaded, total ").Replace("个脚本，开始执行", " script(s), starting");
        }

        if (s.StartsWith("自动秘境：", StringComparison.Ordinal))
            return "Auto Domain: " + TranslateToken(s.Substring("自动秘境：".Length));

        if (s.StartsWith("自动剧情：", StringComparison.Ordinal))
            return "Auto-Skip: " + TranslateToken(s.Substring("自动剧情：".Length));

        if (s.StartsWith("匹配到战斗脚本：", StringComparison.Ordinal))
            return "Matched combat script: " + TranslateToken(s.Substring("匹配到战斗脚本：".Length));

        if (s.StartsWith("路线 ", StringComparison.Ordinal) && s.Contains(" 检测失败"))
        {
            m = Regex.Match(s, @"^路线\s+(.+?)\s+第\s*(\d+)\s*次检测失败\s*\(距离\s*([^)]+)\)\s*——\s*当前\(([^)]+)\)\s*目标\(([^)]+)\)$");
            if (m.Success)
                return "Route " + TranslateGlossary(m.Groups[1].Value) + " check #" + m.Groups[2].Value + " failed (dist " + m.Groups[3].Value + ") -- cur(" + m.Groups[4].Value + ") target(" + m.Groups[5].Value + ")";
        }

        if (s.StartsWith("当前进度：", StringComparison.Ordinal))
        {
            return s.Replace("当前进度：第 ", "Progress: Group ")
                    .Replace(" 组第 ", " item ")
                    .Replace(" 个", "")
                    .Replace("已完成，该组预计剩余: ", " done, est. remaining: ")
                    .Replace(" 时 ", "h ")
                    .Replace(" 分 ", "m ")
                    .Replace(" 秒", "s");
        }

        if (s.StartsWith("距离过远", StringComparison.Ordinal))
        {
            return s.Replace("距离过远", "Distance too far ")
                    .Replace("，重试多次后仍然失败，放弃此路径点！", ", failed after retries, skipping waypoint!")
                    .Replace("，重试", ", retrying");
        }

        if (s.StartsWith("调整视角，", StringComparison.Ordinal))
            return s.Replace("调整视角，", "Adjusting camera: ");

        if (s.StartsWith("视野调整成功，图标角度: ", StringComparison.Ordinal))
            return s.Replace("视野调整成功，图标角度: ", "Camera aligned, icon angle: ").Replace("，在10°范围内", " (within 10 deg)");

        if (s.StartsWith("图标位置: ", StringComparison.Ordinal))
            return s.Replace("图标位置: ", "Icon pos: ").Replace("角度: ", "angle: ");

        if (s.StartsWith("无法原速重放事件", StringComparison.Ordinal))
            return s.Replace("无法原速重放事件", "Cannot replay event at 1x speed ").Replace("，落后", ", behind by ");

        if (s.StartsWith("5秒后恢复自动拾取:", StringComparison.Ordinal))
            return "Resuming auto-pick in 5s: " + s.Substring("5秒后恢复自动拾取:".Length);

        if (s.StartsWith("恢复自动拾取:", StringComparison.Ordinal))
            return "Auto-pick restored: " + s.Substring("恢复自动拾取:".Length);

        if (s.StartsWith("周本一条龙任务已经不在执行周期", StringComparison.Ordinal))
            return s.Replace("周本一条龙任务已经不在执行周期", "Weekly Boss task not on scheduled day ").Replace("，将跳过此任务！", ", skipping!");

        // Serilog TextToken fragments (trailing spaces / partial phrases)
        if (s == " 加载完成，共") return " loaded, total ";
        if (s == "个脚本，开始执行") return " script(s), starting";
        if (s == " 执行结束") return " finished";
        if (s == " 元素战技，cd:") return " Elemental Skill, CD: ";
        if (s == " 点按元素战技，cd:") return " Tap Elemental Skill, CD: ";
        if (s == " 长按元素战技，cd:") return " Hold Elemental Skill, CD: ";
        if (s == " 秒") return "s";
        if (s == "秒（兜底设置）") return "s (fallback)";
        if (s == " 秒）") return "s)";
        if (s == " -> 远程 ") return " -> remote ";
        if (s == " 个, 失败 ") return ", failed ";
        if (s == " 个") return "";
        if (s == " 七天神像") return " Statue of The Seven";

        return null;
    }

    internal static string TranslateGlossary(string s)
    {
        if (string.IsNullOrEmpty(s) || !ContainsCjk(s)) return s;
        EnsureDictLoaded();
        if (Dict == null || Dict.Count == 0) return s;

        string result = s;
        // Replace longest known glossary / dictionary terms (2..16 chars)
        // Build cached sorted term list once? Let's keep a static sorted array of short CJK terms!
        GlossaryEntry[] entries = GetGlossaryEntries();
        for (int i = 0; i < entries.Length; i++)
        {
            if (result.IndexOf(entries[i].Zh, StringComparison.Ordinal) >= 0)
            {
                result = result.Replace(entries[i].Zh, entries[i].En);
                if (!ContainsCjk(result)) break;
            }
        }
        return result;
    }

    private struct GlossaryEntry
    {
        public string Zh;
        public string En;
        public GlossaryEntry(string zh, string en) { Zh = zh; En = en; }
    }

    private static GlossaryEntry[] _glossaryCache;

    private static GlossaryEntry[] GetGlossaryEntries()
    {
        if (_glossaryCache != null) return _glossaryCache;
        lock (SyncRoot)
        {
            if (_glossaryCache != null) return _glossaryCache;
            var list = new List<GlossaryEntry>();
            if (Dict != null)
            {
                foreach (KeyValuePair<string, string> kv in Dict)
                {
                    if (kv.Key.Length >= 2 && kv.Key.Length <= 24 && ContainsCjk(kv.Key) && !string.IsNullOrEmpty(kv.Value) && !ContainsCjk(kv.Value))
                    {
                        // Skip keys that look like sentences with punctuation or format placeholders
                        if (kv.Key.IndexOf('{') >= 0 || kv.Key.IndexOf('\n') >= 0) continue;
                        list.Add(new GlossaryEntry(kv.Key, kv.Value));
                    }
                }
            }
            list.Sort(delegate(GlossaryEntry a, GlossaryEntry b) { return b.Zh.Length.CompareTo(a.Zh.Length); });
            _glossaryCache = list.ToArray();
            return _glossaryCache;
        }
    }

    private static string TranslateChildSessionString(string s)
    {
        if (s.IndexOf(" | RDP：", StringComparison.Ordinal) >= 0 ||
            s.IndexOf(" | 桌面分身会话：", StringComparison.Ordinal) >= 0 ||
            s.IndexOf(" | 功能已启用：", StringComparison.Ordinal) >= 0)
        {
            string[] segments = s.Split(new string[] { " | " }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = TranslateString(segments[i].Trim());
            }
            return string.Join(" | ", segments);
        }

        if (s.StartsWith("RDP：", StringComparison.Ordinal))
            return "RDP: " + TranslateString(s.Substring("RDP：".Length).Trim());

        if (s.StartsWith("桌面分身会话：", StringComparison.Ordinal))
            return "Virtual Desktop Session: " + TranslateString(s.Substring("桌面分身会话：".Length).Trim());

        if (s.StartsWith("功能已启用：", StringComparison.Ordinal))
            return "Features Enabled: " + TranslateString(s.Substring("功能已启用：".Length).Trim());

        if (s.StartsWith("桌面分身会话 ", StringComparison.Ordinal))
        {
            string rest = s.Substring("桌面分身会话 ".Length);
            if (rest.EndsWith("（运行中）", StringComparison.Ordinal))
                return "Virtual Desktop Session " + rest.Substring(0, rest.Length - 5) + " (Running)";
            return "Virtual Desktop Session " + rest;
        }

        if (s.StartsWith("未知连接状态 ", StringComparison.Ordinal))
            return "Unknown Connection State " + s.Substring("未知连接状态 ".Length);

        if (s.StartsWith("RDP 状态未知（", StringComparison.Ordinal) && s.EndsWith("）", StringComparison.Ordinal))
            return "RDP Status Unknown (" + s.Substring(9, s.Length - 10) + ")";

        if (s.StartsWith("RDP 登录阶段：", StringComparison.Ordinal))
            return "RDP Sign-In Stage: " + s.Substring("RDP 登录阶段：".Length);

        if (s.StartsWith("RDP 登录失败：", StringComparison.Ordinal))
            return "RDP Sign-In Failed: " + s.Substring("RDP 登录失败：".Length);

        if (s.StartsWith("RDP 客户端发生致命错误：", StringComparison.Ordinal))
            return "RDP Client Fatal Error: " + s.Substring("RDP 客户端发生致命错误：".Length);

        if (s.StartsWith("RDP 自动重新连接失败：", StringComparison.Ordinal))
            return "RDP Auto-Reconnect Failed: " + s.Substring("RDP 自动重新连接失败：".Length);

        if (s.StartsWith("RDP 自动重连暂未成功，", StringComparison.Ordinal))
            return "RDP auto-reconnect unsuccessful; " + s.Substring("RDP 自动重连暂未成功，".Length).Replace(" 秒后重试", "s until retry");

        if (s.StartsWith("桌面分身首次初始化尚未完成，", StringComparison.Ordinal))
            return "Virtual Desktop initial setup not finished yet; " + s.Substring("桌面分身首次初始化尚未完成，".Length).Replace(" 秒后自动重试", "s until auto-retry");

        if (s.StartsWith("正在重试桌面分身连接（", StringComparison.Ordinal))
            return "Retrying Virtual Desktop connection (" + s.Substring("正在重试桌面分身连接（".Length).Replace("）", ")");

        if (s.StartsWith("正在重试 RDP 自动连接（", StringComparison.Ordinal))
            return "Retrying RDP auto-connection (" + s.Substring("正在重试 RDP 自动连接（".Length).Replace("）", ")");

        if (s.StartsWith("桌面分身连接及登录初始化未能在 ", StringComparison.Ordinal) && s.EndsWith(" 秒内完成。", StringComparison.Ordinal))
            return "Virtual Desktop connection and sign-in did not complete within " + s.Substring(16, s.Length - 22).Trim() + " seconds.";

        if (s.StartsWith("桌面分身尚未完全连接，无法发送 ", StringComparison.Ordinal))
            return "Virtual Desktop is not fully connected yet; cannot send " + s.Substring("桌面分身尚未完全连接，无法发送 ".Length);

        if (s.StartsWith("向 Child Session 发送 ", StringComparison.Ordinal))
            return "Send " + s.Substring("向 Child Session 发送 ".Length) + " to Child Session";

        if (s.StartsWith("系统组合键已改为在", StringComparison.Ordinal))
        {
            return s.Replace("系统组合键已改为在", "System shortcuts now apply on ")
                    .Replace("本机生效", "your local PC")
                    .Replace("桌面分身生效", "the Virtual Desktop")
                    .Replace("，将在下次 RDP 连接后应用", " (applies on next RDP connection)")
                    .Replace("，正在自动重新连接 RDP", " (auto-reconnecting RDP)");
        }

        if (s.StartsWith("已在桌面分身（会话 ", StringComparison.Ordinal))
        {
            return s.Replace("已在桌面分身（会话 ", "Launched as Administrator in Virtual Desktop (Session ")
                    .Replace("）中以管理员权限启动 ", "): ");
        }

        if (s.StartsWith("正在以管理员权限启动 ", StringComparison.Ordinal))
            return "Starting " + s.Substring("正在以管理员权限启动 ".Length) + " as Administrator";

        if (s.StartsWith("正在注销桌面分身会话 ", StringComparison.Ordinal))
            return "Signing out Virtual Desktop session " + s.Substring("正在注销桌面分身会话 ".Length);

        if (s.EndsWith(" 已注销，桌面分身窗口已隐藏", StringComparison.Ordinal))
            return s.Substring(0, s.Length - " 已注销，桌面分身窗口已隐藏".Length) + " signed out; Virtual Desktop window hidden";

        if (s.StartsWith("无法注销 Child Session ", StringComparison.Ordinal))
            return s.Replace("无法注销 Child Session ", "Failed to sign out Child Session ").Replace("（Win32 错误 ", " (Win32 Error ").Replace("）", ")");

        if (s.StartsWith("无法取得 RDP Child Session ID（Win32 错误 ", StringComparison.Ordinal))
            return s.Replace("无法取得 RDP Child Session ID（Win32 错误 ", "Failed to get RDP Child Session ID (Win32 Error ").Replace("）", ")");

        if (s.StartsWith("桌面分身环境预检：", StringComparison.Ordinal))
            return "Virtual Desktop pre-check: " + s.Substring("桌面分身环境预检：".Length);

        if (s.StartsWith("桌面分身 BetterGI 已连接根实例：", StringComparison.Ordinal))
            return s.Replace("桌面分身 BetterGI 已连接根实例：", "Virtual Desktop BetterGI connected to root instance: ").Replace("进程 ", "PID ").Replace("，Session ", ", Session ");

        if (s.StartsWith("桌面分身 BetterGI 已断开：", StringComparison.Ordinal))
            return s.Replace("桌面分身 BetterGI 已断开：", "Virtual Desktop BetterGI disconnected: ").Replace("进程 ", "PID ").Replace("，Session ", ", Session ");

        if (s.StartsWith("桌面分身 RDP 连接失败：", StringComparison.Ordinal))
            return s.Replace("桌面分身 RDP 连接失败：", "Virtual Desktop RDP connection failed: ").Replace("，错误代码：", ", ErrorCode: ").Replace("，扩展错误代码：", ", ExtendedErrorCode: ");

        if (s.StartsWith("桌面分身首次初始化连接失败，", StringComparison.Ordinal) ||
            s.StartsWith("RDP 设置切换后的自动重连失败，", StringComparison.Ordinal))
        {
            return s.Replace("桌面分身首次初始化连接失败，将在 ", "Virtual Desktop initial connection failed; retrying in ")
                    .Replace("RDP 设置切换后的自动重连失败，将在 ", "RDP auto-reconnect after setting change failed; retrying in ")
                    .Replace(" 秒后进行第 ", "s (attempt #")
                    .Replace(" 次重试。错误：", "). Error: ")
                    .Replace("，错误代码：", ", ErrorCode: ")
                    .Replace("，扩展错误代码：", ", ExtendedErrorCode: ");
        }

        if (s.StartsWith("自动启动 BetterGI 失败：", StringComparison.Ordinal))
            return "Failed to auto-launch BetterGI: " + s.Substring("自动启动 BetterGI 失败：".Length);

        if (s.StartsWith("重试桌面分身连接失败：", StringComparison.Ordinal))
            return "Failed to retry Virtual Desktop connection: " + s.Substring("重试桌面分身连接失败：".Length);

        if (s.StartsWith("注册表 fDenyTSConnections = 1（", StringComparison.Ordinal))
        {
            return s.Replace("注册表 fDenyTSConnections = 1（", "Registry fDenyTSConnections = 1 (")
                    .Replace("本机设置", "Local Setting")
                    .Replace("组策略", "Group Policy")
                    .Replace("），本机 RDP 监听器不会启动，桌面分身可能无法建立会话。", "); the local RDP listener will not start and Virtual Desktop may fail to create a session.");
        }

        if (s.IndexOf("\n\n断开原因：", StringComparison.Ordinal) >= 0 ||
            s.IndexOf("\n\n检测到可能影响桌面分身的环境设置。", StringComparison.Ordinal) >= 0 ||
            s.IndexOf("RDP ActiveX 最后报告：", StringComparison.Ordinal) >= 0)
        {
            return s.Replace("RDP 连接意外断开", "RDP Connection Unexpectedly Disconnected")
                    .Replace("RDP 连接失败", "RDP Connection Failed")
                    .Replace("断开原因：", "Disconnect Reason: ")
                    .Replace("扩展原因：", "Extended Reason: ")
                    .Replace("错误代码：", "Error Code: ")
                    .Replace("RDP ActiveX 最后报告：", "Last RDP ActiveX Report:")
                    .Replace("RDP ActiveX 未报告更具体的失败原因。", "RDP ActiveX did not report a more specific failure reason.")
                    .Replace("操作被系统拒绝，请确认 BetterGI 正在以管理员权限运行。", "Operation denied by the system. Please ensure BetterGI is running as Administrator.")
                    .Replace("检测到可能影响桌面分身的环境设置。是否仍要继续？", "Detected environment settings that may affect Virtual Desktop. Continue anyway?");
        }

        return null;
    }

    internal static void RefreshWpfProperty(object depObj, object dp)
    {
        if (depObj == null || dp == null || InvalidatePropMethod == null || DispatcherProp == null || BeginInvokeMethod == null)
            return;
        try
        {
            object disp = DispatcherProp.GetValue(depObj, null);
            if (disp == null) return;
            Action act = delegate
            {
                try { InvalidatePropMethod.Invoke(depObj, new object[] { dp }); }
                catch { }
            };
            BeginInvokeMethod.Invoke(disp, new object[] { act, new object[0] });
        }
        catch
        {
        }
    }

    private sealed class CoerceWrapper
    {
        private readonly Delegate _existing;
        private readonly object _dp;

        public CoerceWrapper(Delegate existing, object dp)
        {
            _existing = existing;
            _dp = dp;
        }

        public object Invoke(object d, object baseValue)
        {
            object val = baseValue;
            try
            {
                if (_existing != null)
                {
                    val = _existing.DynamicInvoke(new object[] { d, val });
                }
            }
            catch
            {
            }

            string str = val as string;
            if (str != null)
            {
                if (ContentDp != null && object.ReferenceEquals(_dp, ContentDp) && !IsSafeContentControl(d))
                {
                    return val;
                }
                return TranslateString(str, d, _dp, true);
            }
            return val;
        }
    }
}

internal static class AutoTranslator
{
    private struct WpfTarget
    {
        public WeakReference ObjRef;
        public object Dp;
    }

    private struct WebTarget
    {
        public WeakReference CoreRef;
    }

    private static readonly object LockObj = new object();
    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly HashSet<string> Attempted = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Queue<string> WorkQueue = new Queue<string>();
    private static readonly Dictionary<string, List<WpfTarget>> PendingWpf = new Dictionary<string, List<WpfTarget>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<WebTarget>> PendingWeb = new Dictionary<string, List<WebTarget>>(StringComparer.Ordinal);

    private static string _cachePath;
    private static bool _cacheLoaded;
    private static bool _cacheDirty;
    private static int _started;
    private static DateTime _lastFlush = DateTime.UtcNow;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        EnsureCacheLoaded();
        Thread t = new Thread(WorkerLoop);
        t.IsBackground = true;
        t.Name = "BgiEnAutoTranslator";
        t.Start();
    }

    private static void EnsureCacheLoaded()
    {
        if (_cacheLoaded) return;
        lock (LockObj)
        {
            if (_cacheLoaded) return;
            try
            {
                _cachePath = StartupHook.ResolveUserI18nFile("auto_cache.json");
                if (!string.IsNullOrEmpty(_cachePath) && File.Exists(_cachePath))
                {
                    string raw = File.ReadAllText(_cachePath, Encoding.UTF8);
                    StartupHook.ParseFlatJson(raw, Cache);
                }
            }
            catch
            {
            }
            _cacheLoaded = true;
        }
    }

    public static string TryGetCached(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        EnsureCacheLoaded();
        lock (LockObj)
        {
            string val;
            if (Cache.TryGetValue(text, out val) && !string.IsNullOrEmpty(val))
                return val;
        }
        return null;
    }

    private static bool ShouldSkipTranslation(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        if (s.Length > 1200) return true;
        if (s.IndexOf(":\\", StringComparison.Ordinal) >= 0) return true;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static void EnqueueWpf(string text, object depObj, object dp)
    {
        if (ShouldSkipTranslation(text)) return;
        EnsureCacheLoaded();
        lock (LockObj)
        {
            if (Cache.ContainsKey(text)) return;

            List<WpfTarget> list;
            if (!PendingWpf.TryGetValue(text, out list))
            {
                list = new List<WpfTarget>();
                PendingWpf[text] = list;
            }
            if (list.Count < 8)
            {
                list.Add(new WpfTarget { ObjRef = new WeakReference(depObj), Dp = dp });
            }

            if (!Attempted.Contains(text))
            {
                Attempted.Add(text);
                if (WorkQueue.Count < 500)
                {
                    WorkQueue.Enqueue(text);
                    Monitor.Pulse(LockObj);
                }
            }
        }
    }

    public static void EnqueueWeb(string text, object coreWebView2)
    {
        if (ShouldSkipTranslation(text)) return;
        EnsureCacheLoaded();
        lock (LockObj)
        {
            if (Cache.ContainsKey(text)) return;

            List<WebTarget> list;
            if (!PendingWeb.TryGetValue(text, out list))
            {
                list = new List<WebTarget>();
                PendingWeb[text] = list;
            }
            bool exists = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (object.ReferenceEquals(list[i].CoreRef.Target, coreWebView2)) { exists = true; break; }
            }
            if (!exists && list.Count < 4)
            {
                list.Add(new WebTarget { CoreRef = new WeakReference(coreWebView2) });
            }

            if (!Attempted.Contains(text))
            {
                Attempted.Add(text);
                if (WorkQueue.Count < 500)
                {
                    WorkQueue.Enqueue(text);
                    Monitor.Pulse(LockObj);
                }
            }
        }
    }

    private static void WorkerLoop()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        }
        catch
        {
        }

        int consecutiveFailures = 0;
        while (true)
        {
            string item = null;
            lock (LockObj)
            {
                while (WorkQueue.Count == 0)
                {
                    if (_cacheDirty && (DateTime.UtcNow - _lastFlush).TotalSeconds >= 5)
                    {
                        FlushCacheLocked();
                    }
                    Monitor.Wait(LockObj, 5000);
                }
                item = WorkQueue.Dequeue();
            }

            if (consecutiveFailures >= 5)
            {
                Thread.Sleep(15000);
                consecutiveFailures = 0;
            }

            string translated = TranslateOnline(item);
            if (!string.IsNullOrEmpty(translated) && !string.Equals(translated, item, StringComparison.Ordinal))
            {
                consecutiveFailures = 0;
                List<WpfTarget> wpfTargets = null;
                List<WebTarget> webTargets = null;
                lock (LockObj)
                {
                    if (Cache.Count < 15000)
                    {
                        Cache[item] = translated;
                        _cacheDirty = true;
                    }
                    PendingWpf.TryGetValue(item, out wpfTargets);
                    PendingWpf.Remove(item);
                    PendingWeb.TryGetValue(item, out webTargets);
                    PendingWeb.Remove(item);
                    if (_cacheDirty && (DateTime.UtcNow - _lastFlush).TotalSeconds >= 5)
                    {
                        FlushCacheLocked();
                    }
                }

                if (wpfTargets != null)
                {
                    for (int i = 0; i < wpfTargets.Count; i++)
                    {
                        object target = wpfTargets[i].ObjRef.Target;
                        if (target != null)
                        {
                            StartupHook.RefreshWpfProperty(target, wpfTargets[i].Dp);
                        }
                    }
                }

                if (webTargets != null)
                {
                    for (int i = 0; i < webTargets.Count; i++)
                    {
                        object core = webTargets[i].CoreRef.Target;
                        if (core != null)
                        {
                            WebRepoTranslator.PushSingleTranslation(core, item, translated);
                        }
                    }
                }
            }
            else
            {
                consecutiveFailures++;
                lock (LockObj)
                {
                    PendingWpf.Remove(item);
                    PendingWeb.Remove(item);
                }
            }

            Thread.Sleep(80);
        }
    }

    private static void FlushCacheLocked()
    {
        if (string.IsNullOrEmpty(_cachePath)) return;
        try
        {
            string dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder(Cache.Count * 48 + 16);
            sb.Append("{\r\n");
            int idx = 0;
            int total = Cache.Count;
            foreach (KeyValuePair<string, string> kv in Cache)
            {
                sb.Append("  \"");
                sb.Append(StartupHook.EscapeJsonString(kv.Key));
                sb.Append("\": \"");
                sb.Append(StartupHook.EscapeJsonString(kv.Value));
                sb.Append('"');
                if (idx < total - 1) sb.Append(',');
                sb.Append("\r\n");
                idx++;
            }
            sb.Append('}');
            File.WriteAllText(_cachePath, sb.ToString(), new UTF8Encoding(false));
            _cacheDirty = false;
            _lastFlush = DateTime.UtcNow;
        }
        catch
        {
        }
    }

    private static string TranslateOnline(string text)
    {
        try
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=" + Uri.EscapeDataString(text);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 4000;
            req.ReadWriteTimeout = 4000;
            req.UserAgent = "Mozilla/5.0";

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
            {
                string body = sr.ReadToEnd();
                return ParseGoogleTranslateResponse(body);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string ParseGoogleTranslateResponse(string json)
    {
        if (string.IsNullOrEmpty(json) || json[0] != '[') return null;
        // Format: [[["translated segment","original segment",null,null,10],...],...]
        var sb = new StringBuilder();
        int i = 1;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '[') return null;
        i++; // enter outer segments array

        while (i < json.Length)
        {
            while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
            if (i >= json.Length || json[i] == ']') break;
            if (json[i] == '[')
            {
                i++; // enter segment [ "tr", "orig", ... ]
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i < json.Length && json[i] == '"')
                {
                    string seg = StartupHook.ReadJsonString(json, ref i);
                    sb.Append(seg);
                }
                // skip rest of segment tuple
                int depth = 1;
                while (i < json.Length && depth > 0)
                {
                    if (json[i] == '"')
                    {
                        StartupHook.ReadJsonString(json, ref i);
                        continue;
                    }
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') depth--;
                    i++;
                }
            }
            else
            {
                break;
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}

internal static class WebRepoTranslator
{
    public static string GetInjectedJs()
    {
        return @"(function(){
  if (window.__bgiEnInstalled) return;
  window.__bgiEnInstalled = true;
  try {
    var curLoc = localStorage.getItem('user-locale');
    if (!curLoc || curLoc.indexOf('zh') === 0) {
      localStorage.setItem('user-locale', 'en-US');
    }
  } catch (e) {}

  var reqSeq = 1;
  var pendingReqs = {};
  var cache = Object.create(null);
  var nodeOrig = new WeakMap();
  var attrOrig = new WeakMap();
  var cjkRe = /[\u4e00-\u9fff\u3400-\u4dbf]/;
  var skipTags = { SCRIPT:1, STYLE:1, CODE:1, PRE:1, TEXTAREA:1, NOSCRIPT:1, SVG:1 };
  var pendingTexts = new Set();
  var flushTimer = null;

  function applyTranslationToNode(node, orig, tr) {
    if (!tr || tr === orig) return;
    if (node.nodeType === 3) {
      if (node.nodeValue !== tr) node.nodeValue = tr;
    } else if (node.nodeType === 1) {
      var a = attrOrig.get(node);
      if (a) {
        if (a.placeholder && a.placeholder === orig) node.setAttribute('placeholder', tr);
        if (a.title && a.title === orig) node.setAttribute('title', tr);
        if (a.alt && a.alt === orig) node.setAttribute('alt', tr);
      }
    }
  }

  function applyMap(map) {
    if (!map) return;
    for (var k in map) {
      if (Object.prototype.hasOwnProperty.call(map, k) && map[k]) {
        cache[k] = map[k];
      }
    }
    scanDom(document.body, false);
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function(ev) {
      try {
        var data = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data;
        if (!data) return;
        if (data.__bgiEnTrResp) {
          var cb = pendingReqs[data.id];
          delete pendingReqs[data.id];
          applyMap(data.map);
          if (cb) cb();
        } else if (data.__bgiEnTrPush) {
          applyMap(data.map);
        }
      } catch (e) {}
    });
  }

  function flushTexts() {
    flushTimer = null;
    if (pendingTexts.size === 0) return;
    if (!window.chrome || !window.chrome.webview) { pendingTexts.clear(); return; }
    var arr = Array.from(pendingTexts);
    pendingTexts.clear();
    for (var i = 0; i < arr.length; i += 60) {
      var batch = arr.slice(i, i + 60);
      var id = reqSeq++;
      try {
        window.chrome.webview.postMessage(JSON.stringify({ __bgiEnTr: true, id: id, texts: batch }));
      } catch (e) {}
    }
  }

  function queueText(s) {
    if (!s || !cjkRe.test(s)) return;
    if (cache[s]) return;
    pendingTexts.add(s);
    if (!flushTimer) {
      flushTimer = setTimeout(flushTexts, 40);
    }
  }

  function handleTextNode(node, enqueueUnknown) {
    var cur = node.nodeValue;
    if (!cur) return;
    var orig = nodeOrig.get(node);
    if (!orig) {
      if (!cjkRe.test(cur)) return;
      orig = cur;
      nodeOrig.set(node, orig);
    } else if (cur !== cache[orig] && cjkRe.test(cur)) {
      orig = cur;
      nodeOrig.set(node, orig);
    }
    var tr = cache[orig];
    if (!tr) {
      var trimmed = orig.trim();
      if (trimmed && cache[trimmed]) {
        tr = orig.replace(trimmed, cache[trimmed]);
        cache[orig] = tr;
      }
    }
    if (tr && tr !== orig) {
      if (node.nodeValue !== tr) node.nodeValue = tr;
    } else if (enqueueUnknown) {
      var trimmed2 = orig.trim();
      if (trimmed2) queueText(trimmed2);
    }
  }

  function handleElementAttrs(el, enqueueUnknown) {
    if (!el || el.nodeType !== 1 || !el.getAttribute) return;
    var attrs = ['placeholder', 'title', 'alt'];
    var rec = attrOrig.get(el) || {};
    var changed = false;
    for (var i = 0; i < attrs.length; i++) {
      var name = attrs[i];
      var val = el.getAttribute(name);
      if (!val) continue;
      if (!rec[name] && cjkRe.test(val)) {
        rec[name] = val;
        changed = true;
      } else if (rec[name] && val !== cache[rec[name]] && cjkRe.test(val)) {
        rec[name] = val;
        changed = true;
      }
      var orig = rec[name];
      if (orig) {
        var tr = cache[orig];
        if (tr && tr !== val) {
          el.setAttribute(name, tr);
        } else if (enqueueUnknown && !tr) {
          queueText(orig);
        }
      }
    }
    if (changed) attrOrig.set(el, rec);
  }

  function scanDom(root, enqueueUnknown) {
    if (!root) return;
    if (root.nodeType === 3) {
      if (!root.parentElement || !skipTags[root.parentElement.tagName]) {
        handleTextNode(root, enqueueUnknown);
      }
      return;
    }
    if (root.nodeType !== 1) return;
    if (skipTags[root.tagName]) return;
    handleElementAttrs(root, enqueueUnknown);
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
      acceptNode: function(n) {
        if (n.nodeType === 1 && skipTags[n.tagName]) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      }
    });
    var cur;
    while ((cur = walker.nextNode())) {
      if (cur.nodeType === 3) handleTextNode(cur, enqueueUnknown);
      else if (cur.nodeType === 1) handleElementAttrs(cur, enqueueUnknown);
    }
  }

  function startObserver() {
    if (!document.body) {
      setTimeout(startObserver, 50);
      return;
    }
    scanDom(document.body, true);
    var mo = new MutationObserver(function(muts) {
      for (var i = 0; i < muts.length; i++) {
        var m = muts[i];
        if (m.type === 'characterData') {
          if (!m.target.parentElement || !skipTags[m.target.parentElement.tagName]) {
            handleTextNode(m.target, true);
          }
        } else if (m.type === 'attributes') {
          handleElementAttrs(m.target, true);
        } else if (m.addedNodes) {
          for (var j = 0; j < m.addedNodes.length; j++) {
            scanDom(m.addedNodes[j], true);
          }
        }
      }
    });
    mo.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['placeholder', 'title', 'alt'] });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', startObserver);
  } else {
    startObserver();
  }
})();";
    }

    public static void HandleMessage(object coreWebView2, string msg)
    {
        try
        {
            Match idMatch = Regex.Match(msg, "\"id\"\\s*:\\s*(\\d+)");
            string idStr = idMatch.Success ? idMatch.Groups[1].Value : "0";

            int arrIdx = msg.IndexOf("\"texts\"", StringComparison.Ordinal);
            if (arrIdx < 0) return;
            int openBracket = msg.IndexOf('[', arrIdx);
            if (openBracket < 0) return;

            var texts = new List<string>();
            int i = openBracket + 1;
            while (i < msg.Length)
            {
                while (i < msg.Length && (char.IsWhiteSpace(msg[i]) || msg[i] == ',')) i++;
                if (i >= msg.Length || msg[i] == ']') break;
                if (msg[i] == '"')
                {
                    texts.Add(StartupHook.ReadJsonString(msg, ref i));
                }
                else
                {
                    break;
                }
            }

            var sb = new StringBuilder(texts.Count * 64 + 64);
            sb.Append("{\"__bgiEnTrResp\":true,\"id\":");
            sb.Append(idStr);
            sb.Append(",\"map\":{");
            bool first = true;
            for (int k = 0; k < texts.Count; k++)
            {
                string orig = texts[k];
                string tr = StartupHook.TranslateString(orig, null, null, false);
                if (StartupHook.ContainsCjk(tr))
                {
                    AutoTranslator.EnqueueWeb(orig, coreWebView2);
                }
                if (!string.IsNullOrEmpty(tr) && !string.Equals(tr, orig, StringComparison.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(StartupHook.EscapeJsonString(orig));
                    sb.Append("\":\"");
                    sb.Append(StartupHook.EscapeJsonString(tr));
                    sb.Append('"');
                }
            }
            sb.Append("}}");

            PostJsonToCore(coreWebView2, sb.ToString());
        }
        catch
        {
        }
    }

    public static void PushSingleTranslation(object coreWebView2, string orig, string translated)
    {
        if (coreWebView2 == null || string.IsNullOrEmpty(orig) || string.IsNullOrEmpty(translated)) return;
        try
        {
            string json = "{\"__bgiEnTrPush\":true,\"map\":{\"" +
                StartupHook.EscapeJsonString(orig) + "\":\"" +
                StartupHook.EscapeJsonString(translated) + "\"}}";
            PostJsonToCore(coreWebView2, json);
        }
        catch
        {
        }
    }

    private static void PostJsonToCore(object coreWebView2, string json)
    {
        try
        {
            // PostWebMessageAsJson must run on the WebView2 UI thread (Dispatcher)
            object app = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "PresentationFramework")
                {
                    Type appType = asm.GetType("System.Windows.Application");
                    if (appType != null)
                    {
                        PropertyInfo curProp = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                        app = curProp != null ? curProp.GetValue(null, null) : null;
                    }
                    break;
                }
            }

            MethodInfo postMethod = coreWebView2.GetType().GetMethod("PostWebMessageAsJson", new Type[] { typeof(string) });
            if (postMethod == null) return;

            if (app != null)
            {
                PropertyInfo dispProp = app.GetType().GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance);
                object disp = dispProp != null ? dispProp.GetValue(app, null) : null;
                if (disp != null)
                {
                    MethodInfo bi = disp.GetType().GetMethod("BeginInvoke", new Type[] { typeof(Delegate), typeof(object[]) });
                    if (bi != null)
                    {
                        Action act = delegate
                        {
                            try { postMethod.Invoke(coreWebView2, new object[] { json }); }
                            catch { }
                        };
                        bi.Invoke(disp, new object[] { act, new object[0] });
                        return;
                    }
                }
            }

            postMethod.Invoke(coreWebView2, new object[] { json });
        }
        catch
        {
        }
    }
}

internal static class GeminiThoughtProxy
{
    private const string ListenPrefix = "http://127.0.0.1:18432/";
    private const string UpstreamBase = "https://generativelanguage.googleapis.com/v1beta/openai";
    private static readonly object CacheLock = new object();
    private static readonly Dictionary<string, string> ExtraContentCache = new Dictionary<string, string>(StringComparer.Ordinal);
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Thread t = new Thread(RunLoop);
        t.IsBackground = true;
        t.Name = "BgiGeminiThoughtProxy";
        t.Start();
    }

    private static void RunLoop()
    {
        HttpListener listener = null;
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            listener = new HttpListener();
            listener.Prefixes.Add(ListenPrefix);
            listener.Start();
        }
        catch
        {
            return;
        }

        while (true)
        {
            try
            {
                HttpListenerContext ctx = listener.GetContext();
                ThreadPool.QueueUserWorkItem(delegate { HandleContext(ctx); });
            }
            catch
            {
                break;
            }
        }
    }

    private static void HandleContext(HttpListenerContext ctx)
    {
        try
        {
            string rawPath = ctx.Request.RawUrl ?? "/";
            if (rawPath.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
                rawPath = rawPath.Substring(3);
            else if (string.Equals(rawPath, "/v1", StringComparison.OrdinalIgnoreCase))
                rawPath = "/";

            string targetUrl = UpstreamBase + rawPath;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(targetUrl);
            req.Method = ctx.Request.HttpMethod;
            req.Timeout = 120000;
            req.ReadWriteTimeout = 120000;

            string auth = ctx.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth))
                req.Headers["Authorization"] = auth;
            if (!string.IsNullOrEmpty(ctx.Request.ContentType))
                req.ContentType = ctx.Request.ContentType;

            if (ctx.Request.HasEntityBody)
            {
                byte[] reqBytes;
                using (var ms = new MemoryStream())
                {
                    ctx.Request.InputStream.CopyTo(ms);
                    reqBytes = ms.ToArray();
                }

                string reqStr = Encoding.UTF8.GetString(reqBytes);
                string patchedReq = InjectThoughtSignatures(reqStr);
                byte[] outBytes = Encoding.UTF8.GetBytes(patchedReq);
                req.ContentLength = outBytes.Length;
                using (Stream rs = req.GetRequestStream())
                {
                    rs.Write(outBytes, 0, outBytes.Length);
                }
            }

            HttpWebResponse upstreamResp = null;
            int statusCode = 200;
            string contentType = "application/json; charset=utf-8";
            byte[] respBytes;

            try
            {
                upstreamResp = (HttpWebResponse)req.GetResponse();
                statusCode = (int)upstreamResp.StatusCode;
                if (!string.IsNullOrEmpty(upstreamResp.ContentType))
                    contentType = upstreamResp.ContentType;
                using (Stream s = upstreamResp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    if (s != null) s.CopyTo(ms);
                    respBytes = ms.ToArray();
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse errResp = wex.Response as HttpWebResponse;
                if (errResp != null)
                {
                    statusCode = (int)errResp.StatusCode;
                    if (!string.IsNullOrEmpty(errResp.ContentType))
                        contentType = errResp.ContentType;
                    using (Stream s = errResp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        if (s != null) s.CopyTo(ms);
                        respBytes = ms.ToArray();
                    }
                    errResp.Close();
                }
                else
                {
                    statusCode = 502;
                    respBytes = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"Upstream connection error\"}}");
                }
            }
            finally
            {
                if (upstreamResp != null) upstreamResp.Close();
            }

            if (statusCode >= 200 && statusCode < 300)
            {
                string respStr = Encoding.UTF8.GetString(respBytes);
                CacheThoughtSignatures(respStr);
            }

            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = respBytes.Length;
            ctx.Response.OutputStream.Write(respBytes, 0, respBytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch
        {
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.OutputStream.Close();
            }
            catch
            {
            }
        }
    }

    private static void CacheThoughtSignatures(string json)
    {
        if (string.IsNullOrEmpty(json) || json.IndexOf("extra_content", StringComparison.Ordinal) < 0)
            return;

        int searchFrom = 0;
        while (searchFrom < json.Length)
        {
            int ecKeyIdx = json.IndexOf("\"extra_content\"", searchFrom, StringComparison.Ordinal);
            if (ecKeyIdx < 0) break;

            int objStart = FindEnclosingObjectStart(json, ecKeyIdx);
            if (objStart < 0)
            {
                searchFrom = ecKeyIdx + 15;
                continue;
            }

            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd < 0) break;

            string objSlice = json.Substring(objStart, objEnd - objStart + 1);
            Match idMatch = Regex.Match(objSlice, "\"id\"\\s*:\\s*\"([^\"]+)\"");
            if (idMatch.Success)
            {
                string callId = idMatch.Groups[1].Value;
                int relEcIdx = objSlice.IndexOf("\"extra_content\"", StringComparison.Ordinal);
                int colonIdx = objSlice.IndexOf(':', relEcIdx + 15);
                if (colonIdx > 0)
                {
                    int valStart = colonIdx + 1;
                    while (valStart < objSlice.Length && char.IsWhiteSpace(objSlice[valStart])) valStart++;
                    if (valStart < objSlice.Length && objSlice[valStart] == '{')
                    {
                        int valEnd = FindMatchingBrace(objSlice, valStart);
                        if (valEnd > valStart)
                        {
                            string ecVal = objSlice.Substring(valStart, valEnd - valStart + 1);
                            lock (CacheLock)
                            {
                                ExtraContentCache[callId] = ecVal;
                            }
                        }
                    }
                }
            }

            searchFrom = objEnd + 1;
        }
    }

    private static string InjectThoughtSignatures(string json)
    {
        if (string.IsNullOrEmpty(json) || json.IndexOf("tool_calls", StringComparison.Ordinal) < 0)
            return json;

        lock (CacheLock)
        {
            if (ExtraContentCache.Count == 0) return json;
        }

        var sb = new StringBuilder(json.Length + 1024);
        int pos = 0;
        while (pos < json.Length)
        {
            int tcKeyIdx = json.IndexOf("\"tool_calls\"", pos, StringComparison.Ordinal);
            if (tcKeyIdx < 0)
            {
                sb.Append(json, pos, json.Length - pos);
                break;
            }

            int arrStart = json.IndexOf('[', tcKeyIdx + 12);
            if (arrStart < 0)
            {
                sb.Append(json, pos, json.Length - pos);
                break;
            }

            int arrEnd = FindMatchingBracket(json, arrStart);
            if (arrEnd < 0)
            {
                sb.Append(json, pos, json.Length - pos);
                break;
            }

            sb.Append(json, pos, arrStart - pos);
            string arrSlice = json.Substring(arrStart, arrEnd - arrStart + 1);
            sb.Append(InjectIntoToolCallsArray(arrSlice));
            pos = arrEnd + 1;
        }

        return sb.ToString();
    }

    private static string InjectIntoToolCallsArray(string arrJson)
    {
        var sb = new StringBuilder(arrJson.Length + 512);
        int i = 0;
        while (i < arrJson.Length)
        {
            if (arrJson[i] == '"')
            {
                int strEnd = SkipJsonString(arrJson, i);
                sb.Append(arrJson, i, strEnd - i + 1);
                i = strEnd + 1;
                continue;
            }

            if (arrJson[i] == '{')
            {
                int objEnd = FindMatchingBrace(arrJson, i);
                if (objEnd < 0)
                {
                    sb.Append(arrJson, i, arrJson.Length - i);
                    break;
                }

                string objStr = arrJson.Substring(i, objEnd - i + 1);
                if (objStr.IndexOf("\"extra_content\"", StringComparison.Ordinal) < 0)
                {
                    Match idMatch = Regex.Match(objStr, "\"id\"\\s*:\\s*\"([^\"]+)\"");
                    if (idMatch.Success)
                    {
                        string callId = idMatch.Groups[1].Value;
                        string ecVal = null;
                        lock (CacheLock)
                        {
                            ExtraContentCache.TryGetValue(callId, out ecVal);
                        }
                        if (!string.IsNullOrEmpty(ecVal))
                        {
                            sb.Append('{');
                            sb.Append("\"extra_content\":");
                            sb.Append(ecVal);
                            sb.Append(',');
                            sb.Append(objStr, 1, objStr.Length - 1);
                            i = objEnd + 1;
                            continue;
                        }
                    }
                }

                sb.Append(objStr);
                i = objEnd + 1;
                continue;
            }

            sb.Append(arrJson[i]);
            i++;
        }
        return sb.ToString();
    }

    private static int FindEnclosingObjectStart(string s, int pos)
    {
        int depth = 0;
        for (int i = pos - 1; i >= 0; i--)
        {
            if (s[i] == '"')
            {
                i--;
                while (i >= 0)
                {
                    if (s[i] == '"')
                    {
                        int bs = 0;
                        int k = i - 1;
                        while (k >= 0 && s[k] == '\\') { bs++; k--; }
                        if ((bs & 1) == 0) break;
                    }
                    i--;
                }
                continue;
            }
            if (s[i] == '}') depth++;
            else if (s[i] == '{')
            {
                if (depth == 0) return i;
                depth--;
            }
        }
        return -1;
    }

    private static int FindMatchingBrace(string s, int openIdx)
    {
        return FindMatchingDelimiter(s, openIdx, '{', '}');
    }

    private static int FindMatchingBracket(string s, int openIdx)
    {
        return FindMatchingDelimiter(s, openIdx, '[', ']');
    }

    private static int FindMatchingDelimiter(string s, int openIdx, char openCh, char closeCh)
    {
        int depth = 0;
        for (int i = openIdx; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"')
            {
                i = SkipJsonString(s, i);
                continue;
            }
            if (c == openCh) depth++;
            else if (c == closeCh)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static int SkipJsonString(string s, int quoteIdx)
    {
        int i = quoteIdx + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (s[i] == '"') return i;
            i++;
        }
        return s.Length - 1;
    }
}
