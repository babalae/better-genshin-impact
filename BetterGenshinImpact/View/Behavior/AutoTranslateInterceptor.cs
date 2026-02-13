using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BetterGenshinImpact.View.Behavior
{
    public static class AutoTranslateInterceptor
    {
        static AutoTranslateInterceptor()
        {
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyElementLoaded),
                true);
            EventManager.RegisterClassHandler(
                typeof(FrameworkContentElement),
                FrameworkContentElement.LoadedEvent,
                new RoutedEventHandler(OnAnyElementLoaded),
                true);
        }

        public static readonly DependencyProperty EnableAutoTranslateProperty =
            DependencyProperty.RegisterAttached(
                "EnableAutoTranslate",
                typeof(bool),
                typeof(AutoTranslateInterceptor),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.Inherits,
                    OnEnableAutoTranslateChanged));

        public static void SetEnableAutoTranslate(DependencyObject element, bool value)
            => element.SetValue(EnableAutoTranslateProperty, value);

        public static bool GetEnableAutoTranslate(DependencyObject element)
            => (bool)element.GetValue(EnableAutoTranslateProperty);

        private static readonly DependencyProperty ScopeProperty =
            DependencyProperty.RegisterAttached(
                "Scope",
                typeof(Scope),
                typeof(AutoTranslateInterceptor),
                new PropertyMetadata(null));

        private static readonly DependencyProperty OriginalValuesProperty =
            DependencyProperty.RegisterAttached(
                "OriginalValues",
                typeof(Dictionary<DependencyProperty, string>),
                typeof(AutoTranslateInterceptor),
                new PropertyMetadata(null));

        private static Dictionary<DependencyProperty, string>? GetOriginalValuesMap(DependencyObject obj)
            => (Dictionary<DependencyProperty, string>?)obj.GetValue(OriginalValuesProperty);

        private static Dictionary<DependencyProperty, string> GetOrCreateOriginalValuesMap(DependencyObject obj)
        {
            var map = GetOriginalValuesMap(obj);
            if (map != null)
            {
                return map;
            }

            map = new Dictionary<DependencyProperty, string>();
            obj.SetValue(OriginalValuesProperty, map);
            return map;
        }

        private static void OnAnyElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DependencyObject obj)
            {
                return;
            }

            FindNearestScope(obj)?.RequestApply(obj);
        }

        private static Scope? FindNearestScope(DependencyObject obj)
        {
            DependencyObject? current = obj;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.GetValue(ScopeProperty) is Scope scope)
                {
                    return scope;
                }

                current = GetParentObject(current);
            }

            return null;
        }

        private static DependencyObject? GetParentObject(DependencyObject obj)
        {
            if (obj is FrameworkElement fe)
            {
                if (fe.Parent != null)
                {
                    return fe.Parent;
                }

                if (fe.TemplatedParent is DependencyObject templatedParent)
                {
                    return templatedParent;
                }
            }

            if (obj is FrameworkContentElement fce)
            {
                if (fce.Parent != null)
                {
                    return fce.Parent;
                }

                if (fce.TemplatedParent is DependencyObject templatedParent)
                {
                    return templatedParent;
                }
            }

            if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(obj);
            }

            return LogicalTreeHelper.GetParent(obj);
        }

        private static void OnEnableAutoTranslateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe)
            {
                return;
            }

            if (e.NewValue is true)
            {
                if (fe.GetValue(ScopeProperty) is Scope oldScope)
                {
                    fe.Loaded -= oldScope.OnLoaded;
                    fe.Unloaded -= oldScope.OnUnloaded;
                    oldScope.Dispose();
                    fe.ClearValue(ScopeProperty);
                }

                var scope = new Scope(fe);
                fe.SetValue(ScopeProperty, scope);
                fe.Loaded += scope.OnLoaded;
                fe.Unloaded += scope.OnUnloaded;
                if (fe.IsLoaded)
                {
                    scope.ApplyNow();
                }
            }
            else
            {
                if (fe.GetValue(ScopeProperty) is Scope scope)
                {
                    fe.Loaded -= scope.OnLoaded;
                    fe.Unloaded -= scope.OnUnloaded;
                    scope.Dispose();
                    fe.ClearValue(ScopeProperty);
                }
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly FrameworkElement _root;
            private readonly List<Action> _unsubscribe = new();
            private bool _applied;
            private readonly HashSet<ContextMenu> _trackedContextMenus = new();
            private readonly HashSet<ToolTip> _trackedToolTips = new();
            private readonly HashSet<DependencyObject> _pendingApply = new();
            private bool _applyScheduled;
            private bool _refreshScheduled;

            public Scope(FrameworkElement root)
            {
                _root = root;
                WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
                {
                    if (msg.PropertyName == nameof(OtherConfig.UiCultureInfoName))
                    {
                        ScheduleRefresh();
                    }
                });
                _unsubscribe.Add(() => WeakReferenceMessenger.Default.UnregisterAll(this));
            }

            public void OnLoaded(object sender, RoutedEventArgs e)
            {
                if (_applied)
                {
                    return;
                }

                _applied = true;
                Apply(_root);
            }

            public void ApplyNow()
            {
                if (_applied)
                {
                    return;
                }

                _applied = true;
                Apply(_root);
            }

            public void OnUnloaded(object sender, RoutedEventArgs e)
            {
                Dispose();
            }

            public void Dispose()
            {
                foreach (var unsub in _unsubscribe)
                {
                    try
                    {
                        unsub();
                    }
                    catch
                    {
                    }
                }

                _unsubscribe.Clear();
            }

            private void ScheduleRefresh()
            {
                if (!_applied)
                {
                    return;
                }

                if (_refreshScheduled)
                {
                    return;
                }

                _refreshScheduled = true;
                _root.Dispatcher.BeginInvoke(
                    () =>
                    {
                        _refreshScheduled = false;
                        if (!_applied)
                        {
                            return;
                        }

                        RestoreOriginalValues(_root);
                        RefreshBoundValues(_root);
                        Apply(_root);
                    },
                    DispatcherPriority.Loaded);
            }

            public void RequestApply(DependencyObject obj)
            {
                if (!_applied)
                {
                    return;
                }

                if (IsInComboBoxContext(obj))
                {
                    return;
                }

                if (!_pendingApply.Add(obj))
                {
                    return;
                }

                if (_applyScheduled)
                {
                    return;
                }

                _applyScheduled = true;
                _root.Dispatcher.BeginInvoke(
                    () =>
                    {
                        _applyScheduled = false;
                        if (!_applied)
                        {
                            _pendingApply.Clear();
                            return;
                        }

                        var items = _pendingApply.ToArray();
                        _pendingApply.Clear();
                        foreach (var item in items)
                        {
                            Apply(item);
                        }
                    },
                    DispatcherPriority.Loaded);
            }

            private void Apply(DependencyObject root)
            {
                var translator = App.GetService<ITranslationService>();
                if (translator == null)
                {
                    return;
                }

                var culture = translator.GetCurrentCulture();
                if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var queue = new Queue<DependencyObject>();
                var visited = new HashSet<DependencyObject>();
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!visited.Add(current))
                    {
                        continue;
                    }
                    
                    if (IsInGridViewRowPresenter(current))
                    {
                        continue;
                    }

                    if (IsInComboBoxContext(current))
                    {
                        continue;
                    }

                    TranslateKnown(current, translator);

                    if (current is FrameworkElement feCurrent)
                    {
                        TrackContextMenu(feCurrent.ContextMenu, queue);
                        TrackToolTip(feCurrent.ToolTip, queue);
                    }

                    if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                    {
                        var count = VisualTreeHelper.GetChildrenCount(current);
                        for (var i = 0; i < count; i++)
                        {
                            queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                        }
                    }

                    if (current is FrameworkElement || current is FrameworkContentElement)
                    {
                        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                        {
                            queue.Enqueue(child);
                        }
                    }

                    if (current is FrameworkElement fe)
                    {
                        foreach (var inline in EnumerateInlineObjects(fe))
                        {
                            queue.Enqueue(inline);
                        }
                    }
                }
            }

            private void TrackContextMenu(ContextMenu? contextMenu, Queue<DependencyObject> queue)
            {
                if (contextMenu == null || !_trackedContextMenus.Add(contextMenu))
                {
                    return;
                }

                queue.Enqueue(contextMenu);

                RoutedEventHandler? openedHandler = null;
                openedHandler = (_, _) =>
                {
                    _root.Dispatcher.BeginInvoke(
                        () => Apply(contextMenu),
                        DispatcherPriority.Loaded);
                };
                contextMenu.Opened += openedHandler;
                _unsubscribe.Add(() => contextMenu.Opened -= openedHandler);
            }

            private void TrackToolTip(object? toolTip, Queue<DependencyObject> queue)
            {
                if (toolTip is not ToolTip tt || !_trackedToolTips.Add(tt))
                {
                    return;
                }

                queue.Enqueue(tt);

                RoutedEventHandler? openedHandler = null;
                openedHandler = (_, _) =>
                {
                    _root.Dispatcher.BeginInvoke(
                        () => Apply(tt),
                        DispatcherPriority.Loaded);
                };
                tt.Opened += openedHandler;
                _unsubscribe.Add(() => tt.Opened -= openedHandler);
            }

            private static IEnumerable<DependencyObject> EnumerateInlineObjects(FrameworkElement fe)
            {
                if (fe is not TextBlock tb)
                {
                    yield break;
                }

                foreach (var inline in EnumerateInlineObjects(tb.Inlines))
                {
                    yield return inline;
                }
            }

            private static IEnumerable<DependencyObject> EnumerateInlineObjects(InlineCollection inlines)
            {
                foreach (var inline in inlines)
                {
                    yield return inline;

                    if (inline is Span span)
                    {
                        foreach (var nested in EnumerateInlineObjects(span.Inlines))
                        {
                            yield return nested;
                        }
                    }

                    if (inline is InlineUIContainer { Child: DependencyObject child })
                    {
                        yield return child;
                    }
                }
            }

            private void TranslateKnown(DependencyObject obj, ITranslationService translator)
            {
                switch (obj)
                {
                    case TextBlock tb:
                        TranslateIfNotBound(tb, TextBlock.TextProperty, tb.Text, s => tb.Text = s, translator);
                        TranslateToolTip(tb, translator);
                        break;
                    case Run run:
                        TranslateIfNotBound(run, Run.TextProperty, run.Text, s => run.Text = s, translator);
                        break;
                    case HeaderedContentControl hcc:
                        if (hcc.Header is string header)
                        {
                            TranslateIfNotBound(hcc, HeaderedContentControl.HeaderProperty, header, s => hcc.Header = s, translator);
                        }
                        TranslateToolTip(hcc, translator);
                        break;
                    case ContentControl cc:
                        if (cc.Content is string content)
                        {
                            TranslateIfNotBound(cc, ContentControl.ContentProperty, content, s => cc.Content = s, translator);
                        }
                        TranslateToolTip(cc, translator);
                        break;
                    case FrameworkElement fe:
                        TranslateToolTip(fe, translator);
                        break;
                }

                TranslateStringLocalValues(obj, translator);
            }

            private void TranslateToolTip(FrameworkElement fe, ITranslationService translator)
            {
                if (fe.ToolTip is string tip)
                {
                    TranslateIfNotBound(fe, FrameworkElement.ToolTipProperty, tip, s => fe.ToolTip = s, translator);
                }
            }

            private void TranslateStringLocalValues(DependencyObject obj, ITranslationService translator)
            {
                var enumerator = obj.GetLocalValueEnumerator();
                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current;
                    var property = entry.Property;
                    if (property.PropertyType != typeof(string) && property.PropertyType != typeof(object))
                    {
                        continue;
                    }

                    if (BindingOperations.IsDataBound(obj, property))
                    {
                        continue;
                    }

                    if (entry.Value is not string value || string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (!ShouldTranslatePropertyName(property.Name))
                    {
                        continue;
                    }

                    var map = GetOriginalValuesMap(obj);
                    if (map == null || !map.TryGetValue(property, out var original))
                    {
                        if (ContainsHan(value))
                        {
                            map = GetOrCreateOriginalValuesMap(obj);
                            map[property] = value;
                            original = value;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var translated = translator.Translate(original, BuildSourceInfo(obj, property, MissingTextSource.UiStaticLiteral));
                    if (!ReferenceEquals(value, translated) && !string.Equals(value, translated, StringComparison.Ordinal))
                    {
                        obj.SetValue(property, translated);
                    }
                }
            }

            private static bool ShouldTranslatePropertyName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                if (name.EndsWith("Path", StringComparison.Ordinal)
                    || name.EndsWith("MemberPath", StringComparison.Ordinal)
                    || string.Equals(name, "Uid", StringComparison.Ordinal)
                    || string.Equals(name, "Name", StringComparison.Ordinal))
                {
                    return false;
                }

                return name.Contains("Text", StringComparison.Ordinal)
                       || name.Contains("Content", StringComparison.Ordinal)
                       || name.Contains("Header", StringComparison.Ordinal)
                       || name.Contains("ToolTip", StringComparison.Ordinal)
                       || name.Contains("Title", StringComparison.Ordinal)
                       || name.Contains("Subtitle", StringComparison.Ordinal)
                       || name.Contains("Description", StringComparison.Ordinal)
                       || name.Contains("Placeholder", StringComparison.Ordinal)
                       || name.Contains("Label", StringComparison.Ordinal)
                       || name.Contains("Caption", StringComparison.Ordinal);
            }

            private static bool ContainsHan(string text)
            {
                foreach (var ch in text)
                {
                    if (ch is >= '\u4E00' and <= '\u9FFF')
                    {
                        return true;
                    }
                }

                return false;
            }

            private void TranslateIfNotBound(
                DependencyObject obj,
                DependencyProperty property,
                string currentValue,
                Action<string> setter,
                ITranslationService translator)
            {
                if (BindingOperations.IsDataBound(obj, property))
                {
                    return;
                }

                var map = GetOriginalValuesMap(obj);
                if (map == null || !map.TryGetValue(property, out var original))
                {
                    if (ContainsHan(currentValue))
                    {
                        map = GetOrCreateOriginalValuesMap(obj);
                        map[property] = currentValue;
                        original = currentValue;
                    }
                    else
                    {
                        original = currentValue;
                    }
                }

                var translated = translator.Translate(original, BuildSourceInfo(obj, property, MissingTextSource.UiStaticLiteral));
                if (!ReferenceEquals(currentValue, translated) && !string.Equals(currentValue, translated, StringComparison.Ordinal))
                {
                    setter(translated);
                }
            }

            private void RestoreOriginalValues(DependencyObject root)
            {
                var queue = new Queue<DependencyObject>();
                var visited = new HashSet<DependencyObject>();
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!visited.Add(current))
                    {
                        continue;
                    }

                    var map = GetOriginalValuesMap(current);
                    if (map != null)
                    {
                        foreach (var pair in map)
                        {
                            if (BindingOperations.IsDataBound(current, pair.Key))
                            {
                                continue;
                            }

                            current.SetValue(pair.Key, pair.Value);
                        }
                    }

                    if (current is FrameworkElement feCurrent)
                    {
                        if (feCurrent.ContextMenu != null)
                        {
                            queue.Enqueue(feCurrent.ContextMenu);
                        }

                        if (feCurrent.ToolTip is DependencyObject tt)
                        {
                            queue.Enqueue(tt);
                        }
                    }

                    if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                    {
                        var count = VisualTreeHelper.GetChildrenCount(current);
                        for (var i = 0; i < count; i++)
                        {
                            queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                        }
                    }

                    if (current is FrameworkElement || current is FrameworkContentElement)
                    {
                        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                        {
                            queue.Enqueue(child);
                        }
                    }

                    if (current is TextBlock tb)
                    {
                        foreach (var inline in EnumerateInlineObjects(tb.Inlines))
                        {
                            queue.Enqueue(inline);
                        }
                    }
                }
            }

            private static void RefreshBoundValues(DependencyObject root)
            {
                var queue = new Queue<DependencyObject>();
                var visited = new HashSet<DependencyObject>();
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!visited.Add(current))
                    {
                        continue;
                    }

                    RefreshBindings(current);

                    if (current is FrameworkElement feCurrent)
                    {
                        if (feCurrent.ContextMenu != null)
                        {
                            queue.Enqueue(feCurrent.ContextMenu);
                        }

                        if (feCurrent.ToolTip is DependencyObject tt)
                        {
                            queue.Enqueue(tt);
                        }
                    }

                    if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                    {
                        var count = VisualTreeHelper.GetChildrenCount(current);
                        for (var i = 0; i < count; i++)
                        {
                            queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                        }
                    }

                    if (current is FrameworkElement || current is FrameworkContentElement)
                    {
                        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                        {
                            queue.Enqueue(child);
                        }
                    }

                    if (current is TextBlock tb)
                    {
                        foreach (var inline in EnumerateInlineObjects(tb.Inlines))
                        {
                            queue.Enqueue(inline);
                        }
                    }
                }
            }

            private static void RefreshBindings(DependencyObject obj)
            {
                var enumerator = obj.GetLocalValueEnumerator();
                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current;
                    var property = entry.Property;
                    if (!BindingOperations.IsDataBound(obj, property))
                    {
                        continue;
                    }

                    BindingOperations.GetBindingExpressionBase(obj, property)?.UpdateTarget();
                }
            }

            private static TranslationSourceInfo BuildSourceInfo(DependencyObject element, DependencyProperty property, MissingTextSource source)
            {
                var viewElement = FindViewElement(element);
                var viewType = viewElement?.GetType();
                var xamlPath = GetViewXamlPath(viewType);

                return new TranslationSourceInfo
                {
                    Source = source,
                    ViewXamlPath = xamlPath,
                    ViewType = viewType?.FullName,
                    ElementType = element.GetType().FullName,
                    ElementName = GetElementName(element),
                    PropertyName = property.Name
                };
            }

            private static DependencyObject? FindViewElement(DependencyObject? element)
            {
                var current = element;
                while (current != null)
                {
                    if (current is Window || current is Page || current is UserControl)
                    {
                        return current;
                    }

                    var parent = LogicalTreeHelper.GetParent(current);
                    if (parent == null && current is FrameworkElement fe)
                    {
                        parent = fe.Parent ?? fe.TemplatedParent as DependencyObject;
                    }

                    if (parent == null && current is FrameworkContentElement fce)
                    {
                        parent = fce.Parent;
                    }

                    if (parent == null)
                    {
                        parent = VisualTreeHelper.GetParent(current);
                    }

                    current = parent;
                }

                return null;
            }

            private static string? GetViewXamlPath(Type? viewType)
            {
                if (viewType == null)
                {
                    return null;
                }

                var ns = viewType.Namespace ?? string.Empty;
                const string viewMarker = ".View.";
                var index = ns.IndexOf(viewMarker, StringComparison.Ordinal);
                if (index < 0)
                {
                    return null;
                }

                var relativeNamespace = ns[(index + viewMarker.Length)..];
                var folder = string.IsNullOrWhiteSpace(relativeNamespace) ? "View" : $"View/{relativeNamespace.Replace('.', '/')}";
                return $"{folder}/{viewType.Name}.xaml";
            }

            private static string? GetElementName(DependencyObject element)
            {
                return element switch
                {
                    FrameworkElement fe when !string.IsNullOrWhiteSpace(fe.Name) => fe.Name,
                    FrameworkContentElement fce when !string.IsNullOrWhiteSpace(fce.Name) => fce.Name,
                    _ => null
                };
            }
            
            private static bool IsInGridViewRowPresenter(DependencyObject obj)
            {
                DependencyObject? current = obj;
                while (current != null)
                {
                    if (current is GridViewRowPresenter)
                    {
                        return true;
                    }

                    current = GetParentObject(current);
                }

                return false;
            }

            private static bool IsInComboBoxContext(DependencyObject obj)
            {
                DependencyObject? current = obj;
                while (current != null)
                {
                    if (current is ComboBox or ComboBoxItem)
                    {
                        return true;
                    }

                    if (current is Popup { PlacementTarget: ComboBox })
                    {
                        return true;
                    }

                    current = GetParentObject(current);
                }

                return false;
            }
        }
    }
}
