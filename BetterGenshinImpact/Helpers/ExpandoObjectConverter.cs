using System.Text.Json.Serialization;
using System.Text.Json;

namespace BetterGenshinImpact.Helpers;

public class ExpandoObjectConverter
{
    private static readonly JsonSerializerOptions _options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T ConvertTo<T>(dynamic source)
    {
        var json = JsonSerializer.Serialize(source, _options);
        return JsonSerializer.Deserialize<T>(json, _options);
    }
}
