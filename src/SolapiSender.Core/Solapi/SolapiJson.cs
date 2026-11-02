using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolapiSender.Core.Solapi;

internal static class SolapiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientDateTimeOffsetConverter() },
    };

    /// <summary>Chấp nhận null, chuỗi rỗng và mọi định dạng ISO 8601 mà server trả về.</summary>
    private sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return null;
            }
            var s = reader.GetString();
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                ? d
                : null;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(SolapiSigner.FormatDate(value.Value));
        }
    }
}
