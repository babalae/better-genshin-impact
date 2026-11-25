using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public enum QuestTextSegmentKind { Literal, Placeholder, GenderVariant, RubyAnnotation, ControlMarker }
public enum TravelerGender { Male, Female }
public sealed record QuestTextContext(string? PlayerName = null, TravelerGender? Gender = null,
    IReadOnlyDictionary<string, string>? Variables = null);
public sealed record QuestTextSegment(QuestTextSegmentKind Kind, int Start, int Length, string Raw,
    string? VariableName = null, string? SemanticVariable = null, string? Argument = null);

public sealed record QuestTextTemplate(string RawText, IReadOnlyList<QuestTextSegment> Segments)
{
    public static QuestTextTemplate Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var segments = new List<QuestTextSegment>();
        var literalStart = 0;
        var position = 0;
        if (text.StartsWith('#') && (text.Length == 1 || text[1] != '{'))
        {
            segments.Add(new(QuestTextSegmentKind.ControlMarker, 0, 1, "#"));
            position = literalStart = 1;
        }
        while (position < text.Length)
        {
            if (text[position] != '{') { position++; continue; }
            var start = position > literalStart && text[position - 1] == '#' ? position - 1 : position;
            var depth = 1;
            var end = position + 1;
            while (end < text.Length && depth > 0)
            {
                if (text[end] == '{') depth++;
                else if (text[end] == '}') depth--;
                end++;
            }
            if (depth > 0) break; // An incomplete token remains literal, byte-for-byte.
            if (start > literalStart)
                segments.Add(new(QuestTextSegmentKind.Literal, literalStart, start - literalStart, text[literalStart..start]));
            var body = text[(position + 1)..(end - 1)];
            var raw = text[start..end];
            var separator = body.IndexOfAny(['#', '[', '{']);
            var variable = separator < 0 ? body : body[..separator];
            var argument = separator < 0 ? null : body[(separator + (body[separator] == '#' ? 1 : 0))..];
            var kind = variable == "RUBY" && body.StartsWith("RUBY#", StringComparison.Ordinal)
                ? QuestTextSegmentKind.RubyAnnotation
                : variable is "M" or "F" && body.StartsWith(variable + "#", StringComparison.Ordinal)
                    ? QuestTextSegmentKind.GenderVariant : QuestTextSegmentKind.Placeholder;
            var semantic = variable == "NICKNAME" ? "PlayerName"
                : kind == QuestTextSegmentKind.GenderVariant ? "TravelerGender" : null;
            segments.Add(new(kind, start, end - start, raw, variable, semantic, argument));
            position = literalStart = end;
        }
        if (literalStart < text.Length)
            segments.Add(new(QuestTextSegmentKind.Literal, literalStart, text.Length - literalStart, text[literalStart..]));
        return new(text, Array.AsReadOnly(segments.ToArray()));
    }

    /// <summary>Render visible base text; missing/unknown substitutions keep their exact original token.</summary>
    public string Render(QuestTextContext? context = null)
    {
        var output = new StringBuilder(RawText.Length);
        foreach (var segment in Segments)
        {
            switch (segment.Kind)
            {
                case QuestTextSegmentKind.ControlMarker:
                case QuestTextSegmentKind.RubyAnnotation:
                    break; // Both remain available in Segments/RawText for other consumers.
                case QuestTextSegmentKind.GenderVariant when context?.Gender is not null:
                    if ((segment.VariableName == "M") == (context.Gender == TravelerGender.Male))
                        output.Append(segment.Argument);
                    break;
                case QuestTextSegmentKind.Placeholder when segment.SemanticVariable == "PlayerName" &&
                    !string.IsNullOrEmpty(context?.PlayerName):
                    output.Append(context.PlayerName);
                    break;
                case QuestTextSegmentKind.Placeholder when segment.VariableName is not null &&
                    context?.Variables is not null && context.Variables.TryGetValue(segment.VariableName, out var replacement):
                    output.Append(replacement);
                    break;
                default:
                    output.Append(segment.Raw);
                    break;
            }
        }
        return output.ToString();
    }
}
