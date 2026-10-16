using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 保留现有数字配置格式；旧 Gpu、未知枚举值和无法识别的设置只回退推理类型，
/// 不让单个字段的兼容问题导致整份配置读取失败。
/// </summary>
public sealed class InferenceDeviceTypeJsonConverter : JsonConverter<InferenceDeviceType>
{
    public override bool HandleNull => true;

    public override InferenceDeviceType Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            var value = (InferenceDeviceType)number;
            return Enum.IsDefined(value) ? value : InferenceDeviceType.Cpu;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            // 未标记 Flags 的枚举也能解析逗号组合，推理类型只接受一个明确的选项。
            if (name is not null && !name.Contains(',') &&
                Enum.TryParse<InferenceDeviceType>(name, true, out var value) && Enum.IsDefined(value))
                return value;
        }

        // 对错误的对象或数组值消费整个节点，继续读取同一配置中的其他字段。
        reader.Skip();
        return InferenceDeviceType.Cpu;
    }

    public override void Write(Utf8JsonWriter writer, InferenceDeviceType value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(Enum.IsDefined(value) ? (int)value : (int)InferenceDeviceType.Cpu);
    }
}
