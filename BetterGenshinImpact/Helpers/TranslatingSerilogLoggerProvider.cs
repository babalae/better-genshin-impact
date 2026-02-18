using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace BetterGenshinImpact.Helpers;

public sealed class TranslatingSerilogLoggerProvider : ILoggerProvider
{
    private readonly ITranslationService _translationService;

    public TranslatingSerilogLoggerProvider(ITranslationService translationService)
    {
        _translationService = translationService;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new TranslatingSerilogLogger(categoryName, _translationService);
    }

    public void Dispose()
    {
    }

    private sealed class TranslatingSerilogLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly ITranslationService _translationService;
        private readonly Serilog.ILogger _logger;

        public TranslatingSerilogLogger(string categoryName, ITranslationService translationService)
        {
            _translationService = translationService;
            _logger = Serilog.Log.Logger.ForContext("SourceContext", categoryName);
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var serilogLevel = ConvertLevel(logLevel);
            if (serilogLevel == null)
            {
                return;
            }

            var (template, values) = ExtractTemplateAndValues(state, formatter, exception);
            var translatedTemplate = RuntimeHelper.IsDebuggerAttached
                ? template
                : _translationService.Translate(template, TranslationSourceInfo.From(MissingTextSource.Log));

            if (values.Length == 0)
            {
                _logger.Write(serilogLevel.Value, exception, translatedTemplate);
                return;
            }

            _logger.Write(serilogLevel.Value, exception, translatedTemplate, values);
        }

        private (string Template, object?[] Values) ExtractTemplateAndValues<TState>(
            TState state,
            Func<TState, Exception?, string> formatter,
            Exception? exception)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                var original = kvps.FirstOrDefault(kv => string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal));
                var template = original.Value as string;
                if (string.IsNullOrEmpty(template))
                {
                    template = formatter(state, exception);
                }

                var values = kvps
                    .Where(kv =>
                        !string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal) &&
                        !string.Equals(kv.Key, "EventId", StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .ToArray();

                return (template ?? string.Empty, values);
            }

            return (formatter(state, exception), Array.Empty<object?>());
        }

        private static LogEventLevel? ConvertLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Critical => LogEventLevel.Fatal,
                _ => null
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
