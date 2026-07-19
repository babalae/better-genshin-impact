using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using NCalc;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Core.Recognition;

public sealed class RecognitionObjectJsonLoadContext
{
    public required int CaptureWidth { get; init; }

    public required int CaptureHeight { get; init; }

    public required Func<string, ImreadModes, Mat> TemplateLoader { get; init; }

    public IReadOnlyDictionary<string, object>? ExtraParameters { get; init; }
}

public static class RecognitionObjectJsonLoader
{
    private sealed class LoggerTag;

    private static readonly ILogger Logger = App.GetLogger<LoggerTag>();

    public static RecognitionObject LoadFromFile(string filePath, string objectName, RecognitionObjectJsonLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(objectName);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var config = JsonConvert.DeserializeObject<RecognitionObjectJsonFile>(json)
                         ?? throw new InvalidOperationException($"无法解析 RecognitionObject 配置文件: {filePath}");

            return Load(config, objectName, context);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Recognition 加载失败: {ObjectName} @ {CaptureWidth}x{CaptureHeight}, file={FilePath}",
                objectName,
                context.CaptureWidth,
                context.CaptureHeight,
                filePath);
            throw;
        }
    }

    public static RecognitionObject Load(RecognitionObjectJsonFile config, string objectName, RecognitionObjectJsonLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(objectName);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (!config.Objects.TryGetValue(objectName, out var objectConfig))
            {
                throw new KeyNotFoundException($"未找到名称为 {objectName} 的 RecognitionObject 配置");
            }

            return new Loader(config, context).BuildRecognitionObject(objectName, objectConfig);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Recognition 构建失败: {ObjectName} @ {CaptureWidth}x{CaptureHeight}",
                objectName,
                context.CaptureWidth,
                context.CaptureHeight);
            throw;
        }
    }

    private sealed class Loader
    {
        private static readonly string[] RectFunctionNames =
        [
            "rect",
            "cutLeft",
            "cutRight",
            "cutTop",
            "cutBottom",
            "cutLeftTop",
            "cutRightTop",
            "cutLeftBottom",
            "cutRightBottom",
        ];

        private readonly RecognitionObjectJsonFile _config;
        private readonly RecognitionObjectJsonLoadContext _context;
        private readonly Rect _captureRect;
        private readonly double _assetScale;
        private readonly Dictionary<string, double> _resolvedVars = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resolvingVars = new(StringComparer.Ordinal);
        private readonly HashSet<string> _functionNames = new(RectFunctionNames, StringComparer.Ordinal);

        public Loader(RecognitionObjectJsonFile config, RecognitionObjectJsonLoadContext context)
        {
            _config = config;
            _context = context;
            _captureRect = new Rect(0, 0, context.CaptureWidth, context.CaptureHeight);
            _assetScale = context.CaptureWidth < 1920 ? context.CaptureWidth / 1920d : 1d;
        }

        public RecognitionObject BuildRecognitionObject(string objectName, RecognitionObjectJsonConfig config)
        {
            var recognitionType = ParseEnumExact<RecognitionTypes>(config.Type, nameof(config.Type));

            string? templateName = null;
            if (recognitionType == RecognitionTypes.TemplateMatch)
            {
                templateName = ResolveTemplate(config.Template, objectName);
            }

            var recognitionObject = new RecognitionObject
            {
                Name = ResolveName(objectName, config, recognitionType, templateName),
                RecognitionType = recognitionType,
            };

            if (!string.IsNullOrWhiteSpace(config.Roi))
            {
                recognitionObject.RegionOfInterest = EvaluateRect(config.Roi);
            }

            if (config.Threshold.HasValue)
            {
                recognitionObject.Threshold = config.Threshold.Value;
            }

            if (config.Use3Channels.HasValue)
            {
                recognitionObject.Use3Channels = config.Use3Channels.Value;
            }

            if (!string.IsNullOrWhiteSpace(config.TemplateMatchMode))
            {
                recognitionObject.TemplateMatchMode = ParseEnumExact<TemplateMatchModes>(config.TemplateMatchMode, nameof(config.TemplateMatchMode));
            }

            if (config.UseMask.HasValue)
            {
                recognitionObject.UseMask = config.UseMask.Value;
            }

            if (!string.IsNullOrWhiteSpace(config.MaskColor))
            {
                recognitionObject.MaskColor = ParseColor(config.MaskColor);
            }

            if (config.Draw.HasValue)
            {
                recognitionObject.DrawOnWindow = config.Draw.Value;
            }

            if (!string.IsNullOrWhiteSpace(config.DrawColor) || config.DrawWidth.HasValue)
            {
                var drawColor = !string.IsNullOrWhiteSpace(config.DrawColor)
                    ? ParseColor(config.DrawColor)
                    : recognitionObject.DrawOnWindowPen.Color;
                var drawWidth = config.DrawWidth ?? recognitionObject.DrawOnWindowPen.Width;
                recognitionObject.DrawOnWindowPen = new Pen(drawColor, drawWidth);
            }

            if (config.MaxMatchCount.HasValue)
            {
                recognitionObject.MaxMatchCount = config.MaxMatchCount.Value;
            }

            if (config.UseBinaryMatch.HasValue)
            {
                recognitionObject.UseBinaryMatch = config.UseBinaryMatch.Value;
            }

            if (config.BinaryThreshold.HasValue)
            {
                recognitionObject.BinaryThreshold = config.BinaryThreshold.Value;
            }

            if (!string.IsNullOrWhiteSpace(config.ColorCode))
            {
                recognitionObject.ColorConversionCode = ParseEnumExact<ColorConversionCodes>(config.ColorCode, nameof(config.ColorCode));
            }

            if (config.LowerColor is { Count: > 0 })
            {
                recognitionObject.LowerColor = BuildScalar(config.LowerColor, nameof(config.LowerColor));
            }

            if (config.UpperColor is { Count: > 0 })
            {
                recognitionObject.UpperColor = BuildScalar(config.UpperColor, nameof(config.UpperColor));
            }

            if (config.MatchCount.HasValue)
            {
                recognitionObject.MatchCount = config.MatchCount.Value;
            }

            if (!string.IsNullOrWhiteSpace(config.OcrEngine))
            {
                recognitionObject.OcrEngine = ParseEnumExact<OcrEngineTypes>(config.OcrEngine, nameof(config.OcrEngine));
            }

            if (config.Replace.Count > 0)
            {
                recognitionObject.ReplaceDictionary = config.Replace.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToArray(),
                    StringComparer.Ordinal);
            }

            if (config.AllContains.Count > 0)
            {
                recognitionObject.AllContainMatchText = [.. config.AllContains];
            }

            if (config.OneContains.Count > 0)
            {
                recognitionObject.OneContainMatchText = [.. config.OneContains];
            }

            if (config.Regex.Count > 0)
            {
                recognitionObject.RegexMatchText = [.. config.Regex];
            }

            if (!string.IsNullOrWhiteSpace(config.Text))
            {
                recognitionObject.Text = config.Text;
            }

            if (config.Reference?.Size is { Count: 2 })
            {
                recognitionObject.ReferenceImageSize = new OpenCvSharp.Size(config.Reference.Size[0], config.Reference.Size[1]);
            }

            if (!string.IsNullOrWhiteSpace(config.Reference?.Bbox))
            {
                recognitionObject.ReferenceBoundingBox = EvaluateRect(config.Reference.Bbox);
            }

            if (config.Search != null)
            {
                recognitionObject.SearchOptions = new SearchOptions();
                if (!string.IsNullOrWhiteSpace(config.Search.Anchor))
                {
                    recognitionObject.SearchOptions.AnchorMode = ParseEnumExact<SearchAnchorMode>(config.Search.Anchor, nameof(config.Search.Anchor));
                }

                if (config.Search.Expand is { Count: 2 })
                {
                    recognitionObject.SearchOptions.ExpandSize = new OpenCvSharp.Size(config.Search.Expand[0], config.Search.Expand[1]);
                }
            }

            if (recognitionType == RecognitionTypes.TemplateMatch)
            {
                var loadMode = ParseTemplateMode(config.TemplateMode);
                recognitionObject.TemplateImageMat = _context.TemplateLoader(templateName!, loadMode);
            }

            return recognitionObject.InitTemplate();
        }

        private string ResolveName(string objectName, RecognitionObjectJsonConfig config, RecognitionTypes recognitionType, string? templateName)
        {
            if (!string.IsNullOrWhiteSpace(config.Name))
            {
                return config.Name;
            }

            if (recognitionType == RecognitionTypes.TemplateMatch && !string.IsNullOrWhiteSpace(templateName))
            {
                return Path.GetFileNameWithoutExtension(templateName);
            }

            return objectName;
        }

        private ImreadModes ParseTemplateMode(string? templateMode)
        {
            return string.IsNullOrWhiteSpace(templateMode)
                ? ImreadModes.Color
                : ParseEnumExact<ImreadModes>(templateMode, nameof(templateMode));
        }

        private string ResolveTemplate(string? template, string objectName)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException($"对象 {objectName} 缺少 template 配置");
            }

            var resolved = template.Trim();
            while (resolved.StartsWith('@'))
            {
                var alias = resolved[1..];
                if (!_config.Templates.TryGetValue(alias, out resolved))
                {
                    throw new KeyNotFoundException($"未找到模板别名 {alias}");
                }
            }

            return resolved;
        }

        private Rect EvaluateRect(string expression)
        {
            var resolved = ResolveRegionExpression(expression);
            var value = EvaluateValue(resolved);
            return value switch
            {
                Rect rect => rect,
                _ => throw new InvalidOperationException($"表达式 {expression} 未返回 Rect")
            };
        }

        private string ResolveRegionExpression(string expression)
        {
            var resolved = expression.Trim();
            while (resolved.StartsWith('@'))
            {
                var alias = resolved[1..];
                if (!_config.Regions.TryGetValue(alias, out resolved))
                {
                    throw new KeyNotFoundException($"未找到区域别名 {alias}");
                }
            }

            return resolved;
        }

        private object EvaluateValue(string expression, string? skipVarName = null)
        {
            var normalizedExpression = NormalizeExpression(expression);
            var ncalcExpression = new Expression(normalizedExpression);

            foreach (var (name, value) in BuildParameters(skipVarName))
            {
                ncalcExpression.Parameters[name] = value;
            }

            ncalcExpression.Functions["rect"] = args => BuildRect(args.Evaluate(0), args.Evaluate(1), args.Evaluate(2), args.Evaluate(3));
            ncalcExpression.Functions["cutLeft"] = args => _captureRect.CutLeft(ToDouble(args.Evaluate(0)));
            ncalcExpression.Functions["cutRight"] = args => _captureRect.CutRight(ToDouble(args.Evaluate(0)));
            ncalcExpression.Functions["cutTop"] = args => _captureRect.CutTop(ToDouble(args.Evaluate(0)));
            ncalcExpression.Functions["cutBottom"] = args => _captureRect.CutBottom(ToDouble(args.Evaluate(0)));
            ncalcExpression.Functions["cutLeftTop"] = args => _captureRect.CutLeftTop(ToDouble(args.Evaluate(0)), ToDouble(args.Evaluate(1)));
            ncalcExpression.Functions["cutRightTop"] = args => _captureRect.CutRightTop(ToDouble(args.Evaluate(0)), ToDouble(args.Evaluate(1)));
            ncalcExpression.Functions["cutLeftBottom"] = args => _captureRect.CutLeftBottom(ToDouble(args.Evaluate(0)), ToDouble(args.Evaluate(1)));
            ncalcExpression.Functions["cutRightBottom"] = args => _captureRect.CutRightBottom(ToDouble(args.Evaluate(0)), ToDouble(args.Evaluate(1)));

            return ncalcExpression.Evaluate();
        }

        private Dictionary<string, object> BuildParameters(string? skipVarName = null)
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["cw"] = _captureRect.Width,
                ["ch"] = _captureRect.Height,
                ["cx"] = _captureRect.X,
                ["cy"] = _captureRect.Y,
                ["s"] = _assetScale,
            };

            if (_context.ExtraParameters != null)
            {
                foreach (var (name, value) in _context.ExtraParameters)
                {
                    parameters[name] = value;
                }
            }

            foreach (var varName in _config.Vars.Keys)
            {
                if (string.Equals(varName, skipVarName, StringComparison.Ordinal))
                {
                    continue;
                }

                parameters[varName] = ResolveVariable(varName);
            }

            return parameters;
        }

        private double ResolveVariable(string name)
        {
            if (_resolvedVars.TryGetValue(name, out var resolvedValue))
            {
                return resolvedValue;
            }

            if (!_config.Vars.TryGetValue(name, out var expression))
            {
                throw new KeyNotFoundException($"未找到变量 {name}");
            }

            if (!_resolvingVars.Add(name))
            {
                throw new InvalidOperationException($"变量 {name} 存在循环引用");
            }

            try
            {
                var value = ToDouble(EvaluateValue(expression, name));
                _resolvedVars[name] = value;
                return value;
            }
            finally
            {
                _resolvingVars.Remove(name);
            }
        }

        private string NormalizeExpression(string expression)
        {
            var normalized = expression;
            var parameterNames = GetParameterNames()
                .OrderByDescending(name => name.Length)
                .ToList();

            foreach (var parameterName in parameterNames)
            {
                if (_functionNames.Contains(parameterName))
                {
                    continue;
                }

                var pattern = $@"(?<![\w\]]){Regex.Escape(parameterName)}(?![\w\(])";
                normalized = Regex.Replace(normalized, pattern, $"[{parameterName}]");
            }

            return normalized;
        }

        private IEnumerable<string> GetParameterNames()
        {
            yield return "cw";
            yield return "ch";
            yield return "cx";
            yield return "cy";
            yield return "s";

            if (_context.ExtraParameters != null)
            {
                foreach (var name in _context.ExtraParameters.Keys)
                {
                    yield return name;
                }
            }

            foreach (var name in _config.Vars.Keys)
            {
                yield return name;
            }
        }

        private static Rect BuildRect(object? x, object? y, object? width, object? height)
        {
            return new Rect(
                (int)Math.Round(ToDouble(x)),
                (int)Math.Round(ToDouble(y)),
                (int)Math.Round(ToDouble(width)),
                (int)Math.Round(ToDouble(height)));
        }

        private static Scalar BuildScalar(IReadOnlyList<double> values, string fieldName)
        {
            return values.Count switch
            {
                1 => new Scalar(values[0]),
                2 => new Scalar(values[0], values[1]),
                3 => new Scalar(values[0], values[1], values[2]),
                4 => new Scalar(values[0], values[1], values[2], values[3]),
                _ => throw new InvalidOperationException($"{fieldName} 必须是 1 到 4 个数字"),
            };
        }

        private static TEnum ParseEnumExact<TEnum>(string? value, string fieldName) where TEnum : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{fieldName} 不能为空");
            }

            if (Enum.TryParse<TEnum>(value, false, out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"{fieldName} 的值 {value} 不是有效的 {typeof(TEnum).Name}");
        }

        private static Color ParseColor(string value)
        {
            return ColorTranslator.FromHtml(value);
        }

        private static double ToDouble(object? value)
        {
            return value switch
            {
                null => 0d,
                byte byteValue => byteValue,
                short shortValue => shortValue,
                int intValue => intValue,
                long longValue => longValue,
                float floatValue => floatValue,
                double doubleValue => doubleValue,
                decimal decimalValue => (double)decimalValue,
                _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            };
        }
    }
}
