using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.View.Behavior
{
    public static class AutoTranslateInterceptor
    {
        public static readonly DependencyProperty EnableAutoTranslateProperty =
            DependencyProperty.RegisterAttached(
                "EnableAutoTranslate",
                typeof(bool),
                typeof(AutoTranslateInterceptor),
                new PropertyMetadata(false, OnEnableAutoTranslateChanged));

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
            private readonly RoutedEventHandler _anyLoadedHandler;
            private readonly HashSet<ContextMenu> _trackedContextMenus = new();
            private readonly HashSet<ToolTip> _trackedToolTips = new();

            public Scope(FrameworkElement root)
            {
                _root = root;
                _anyLoadedHandler = OnAnyLoaded;
                _root.AddHandler(FrameworkElement.LoadedEvent, _anyLoadedHandler, true);
                _unsubscribe.Add(() => _root.RemoveHandler(FrameworkElement.LoadedEvent, _anyLoadedHandler));
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

            private void OnAnyLoaded(object sender, RoutedEventArgs e)
            {
                if (!_applied)
                {
                    return;
                }

                if (e.OriginalSource is not DependencyObject obj)
                {
                    return;
                }

                _root.Dispatcher.BeginInvoke(
                    () => Apply(obj),
                    DispatcherPriority.Loaded);
            }

            private void Apply(DependencyObject root)
            {
                var translator = App.GetService<ITranslationService>();
                if (translator == null)
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
                    if (property.PropertyType != typeof(string))
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

                    if (!ShouldTranslatePropertyName(property.Name) || !ContainsHan(value))
                    {
                        continue;
                    }

                    var translated = translator.Translate(value, MissingTextSource.UiStaticLiteral);
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

                var translated = translator.Translate(currentValue, MissingTextSource.UiStaticLiteral);
                if (!ReferenceEquals(currentValue, translated) && !string.Equals(currentValue, translated, StringComparison.Ordinal))
                {
                    setter(translated);
                }
            }
        }
    }
}
