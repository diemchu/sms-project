using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SolapiSender.Core.Firebase;

/// <summary>Bỏ qua thuộc tính khi ghi/đọc Firestore.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FirestoreIgnoreAttribute : Attribute;

/// <summary>Thuộc tính nhận ID document (không được ghi thành field).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FirestoreDocumentIdAttribute : Attribute;

/// <summary>
/// Chuyển POCO ⇄ định dạng "Value" của Firestore REST API
/// (stringValue, integerValue, timestampValue, mapValue, arrayValue…).
/// Tên field = [JsonPropertyName] hoặc camelCase. Enum lưu dạng chuỗi camelCase ("active").
/// </summary>
public static class FirestoreMapper
{
    private sealed record PropMap(PropertyInfo Info, string Name, bool IsId);

    private static readonly ConcurrentDictionary<Type, PropMap[]> Cache = new();

    // ------------------------------------------------------------------ write

    public static JsonObject ToFields(object obj)
    {
        var fields = new JsonObject();
        foreach (var p in GetProps(obj.GetType()))
        {
            if (p.IsId || !p.Info.CanRead) continue;
            fields[p.Name] = ToValue(p.Info.GetValue(obj));
        }
        return fields;
    }

    public static JsonObject ToFields(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var fields = new JsonObject();
        foreach (var (k, v) in values) fields[k] = ToValue(v);
        return fields;
    }

    public static JsonObject ToValue(object? value)
    {
        switch (value)
        {
            case null:
                return new JsonObject { ["nullValue"] = null };
            case string s:
                return new JsonObject { ["stringValue"] = s };
            case bool b:
                return new JsonObject { ["booleanValue"] = b };
            case int or long or short or byte or uint:
                return new JsonObject { ["integerValue"] = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) };
            case double or float or decimal:
                return new JsonObject { ["doubleValue"] = Convert.ToDouble(value, CultureInfo.InvariantCulture) };
            case DateTimeOffset dto:
                return new JsonObject { ["timestampValue"] = FormatTimestamp(dto) };
            case DateTime dt:
                return new JsonObject { ["timestampValue"] = FormatTimestamp(new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt)) };
            case Enum e:
                return new JsonObject { ["stringValue"] = EnumToString(e) };
            case IDictionary dict:
            {
                var fields = new JsonObject();
                foreach (DictionaryEntry entry in dict)
                    fields[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)!] = ToValue(entry.Value);
                return new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = fields } };
            }
            case IEnumerable list:
            {
                var values = new JsonArray();
                foreach (var item in list) values.Add(ToValue(item));
                return new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = values } };
            }
            default:
                return new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = ToFields(value) } };
        }
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ read

    /// <summary>Đọc document REST ({ name, fields, createTime, updateTime }) thành T.</summary>
    public static T FromDocument<T>(JsonElement document) where T : new()
    {
        var obj = document.TryGetProperty("fields", out var fields) ? FromFields<T>(fields) : new T();
        if (document.TryGetProperty("name", out var name))
        {
            var id = name.GetString()?.Split('/').LastOrDefault();
            foreach (var p in GetProps(typeof(T)).Where(p => p.IsId && p.Info.CanWrite))
                p.Info.SetValue(obj, id);
        }
        return obj;
    }

    public static T FromFields<T>(JsonElement fields) where T : new() => (T)FromFields(fields, typeof(T));

    private static object FromFields(JsonElement fields, Type type)
    {
        var obj = Activator.CreateInstance(type)!;
        foreach (var p in GetProps(type))
        {
            if (p.IsId || !p.Info.CanWrite) continue;
            if (fields.TryGetProperty(p.Name, out var v))
                p.Info.SetValue(obj, FromValue(v, p.Info.PropertyType));
        }
        return obj;
    }

    public static object? FromValue(JsonElement value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target);
        var t = underlying ?? target;
        string? kind = null;
        JsonElement raw = default;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in value.EnumerateObject())
            {
                kind = p.Name;
                raw = p.Value;
                break;
            }
        }
        var prop = (Name: kind, Value: raw);

        switch (prop.Name)
        {
            case "nullValue":
            case null:
                return target.IsValueType && underlying is null ? Activator.CreateInstance(target) : null;

            case "stringValue":
            {
                var s = prop.Value.GetString() ?? "";
                if (t.IsEnum) return ParseEnum(t, s);
                if (t == typeof(DateTimeOffset)) return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
                return ConvertScalar(s, t);
            }

            case "integerValue":
            {
                var n = long.Parse(prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString()! : prop.Value.GetRawText(),
                    CultureInfo.InvariantCulture);
                return ConvertScalar(n, t);
            }

            case "doubleValue":
                return ConvertScalar(prop.Value.GetDouble(), t);

            case "booleanValue":
                return ConvertScalar(prop.Value.GetBoolean(), t);

            case "timestampValue":
            {
                var dto = DateTimeOffset.Parse(prop.Value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                if (t == typeof(DateTime)) return dto.UtcDateTime;
                if (t == typeof(string)) return dto.ToString("o");
                return dto;
            }

            case "mapValue":
            {
                var fields = prop.Value.TryGetProperty("fields", out var f) ? f : default;
                return ReadMap(fields, t);
            }

            case "arrayValue":
            {
                var items = prop.Value.TryGetProperty("values", out var arr) ? arr.EnumerateArray().ToList() : [];
                return ReadArray(items, t);
            }

            default:
                return null;
        }
    }

    private static object? ReadMap(JsonElement fields, Type t)
    {
        if (t == typeof(object) || IsDictionary(t))
        {
            var valueType = IsDictionary(t) ? t.GetGenericArguments()[1] : typeof(object);
            var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
            if (fields.ValueKind == JsonValueKind.Object)
                foreach (var f in fields.EnumerateObject())
                    dict[f.Name] = FromValue(f.Value, valueType);
            return dict;
        }
        return fields.ValueKind == JsonValueKind.Object ? FromFields(fields, t) : Activator.CreateInstance(t);
    }

    private static object ReadArray(List<JsonElement> items, Type t)
    {
        var itemType = t.IsArray ? t.GetElementType()!
            : t.IsGenericType ? t.GetGenericArguments()[0]
            : typeof(object);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
        foreach (var item in items) list.Add(FromValue(item, itemType));
        if (!t.IsArray) return list;
        var array = Array.CreateInstance(itemType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }

    private static object? ConvertScalar(object value, Type t)
    {
        if (t == typeof(object) || t.IsInstanceOfType(value)) return value;
        if (t == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
        return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
    }

    private static bool IsDictionary(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() is var g
        && (g == typeof(Dictionary<,>) || g == typeof(IDictionary<,>) || g == typeof(IReadOnlyDictionary<,>));

    private static string EnumToString(Enum e)
    {
        var s = e.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    private static object ParseEnum(Type t, string s) =>
        Enum.TryParse(t, s, ignoreCase: true, out var result) ? result! : Activator.CreateInstance(t)!;

    private static PropMap[] GetProps(Type type) => Cache.GetOrAdd(type, static t =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0
                        && p.GetCustomAttribute<FirestoreIgnoreAttribute>() is null
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => new PropMap(
                p,
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                p.GetCustomAttribute<FirestoreDocumentIdAttribute>() is not null))
            .ToArray());
}
