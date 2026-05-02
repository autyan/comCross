using System.Text.Json;

namespace ComCross.Shared.Services;

/// <summary>
/// Minimal, dependency-free JSON Schema validation.
///
/// Goal: "形式验证" for schema-driven plugin parameters.
/// - Validates that schema JSON is well-formed.
/// - Validates object/array/scalar types.
/// - Validates required properties.
/// - Validates enum values (exact JSON match).
///
/// Not a full JSON Schema implementation.
/// </summary>
public static class JsonSchemaLiteValidator
{
    public static bool TryParseSchema(string? schemaJson, out JsonElement schema, out string? error)
    {
        schema = default;
        error = null;

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            error = "Schema is empty.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            schema = doc.RootElement.Clone();
            if (schema.ValueKind != JsonValueKind.Object)
            {
                error = "Schema must be a JSON object.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryValidate(JsonElement schema, JsonElement instance, out string? error)
    {
        error = null;

        if (schema.ValueKind != JsonValueKind.Object)
        {
            error = "Schema must be a JSON object.";
            return false;
        }

        if (schema.TryGetProperty("type", out var typeNode))
        {
            if (!IsInstanceTypeAllowed(typeNode, instance, out var typeError))
            {
                error = typeError;
                return false;
            }
        }

        if (schema.TryGetProperty("enum", out var enumNode) && enumNode.ValueKind == JsonValueKind.Array)
        {
            var ok = false;
            foreach (var candidate in enumNode.EnumerateArray())
            {
                if (JsonElementDeepEquals(candidate, instance))
                {
                    ok = true;
                    break;
                }
            }

            if (!ok)
            {
                error = "Value is not in enum.";
                return false;
            }
        }

        // Only validate object-specific constraints when instance is an object.
        if (instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var requiredNode) && requiredNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var req in requiredNode.EnumerateArray())
                {
                    if (req.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var name = req.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (!instance.TryGetProperty(name, out _))
                    {
                        error = $"Missing required property: {name}";
                        return false;
                    }
                }
            }

            if (schema.TryGetProperty("properties", out var propertiesNode) && propertiesNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propertiesNode.EnumerateObject())
                {
                    if (!instance.TryGetProperty(prop.Name, out var value))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (prop.Value.TryGetProperty("type", out var propTypeNode))
                    {
                        if (!IsInstanceTypeAllowed(propTypeNode, value, out var propTypeError))
                        {
                            error = $"Property '{prop.Name}': {propTypeError}";
                            return false;
                        }
                    }

                    if (prop.Value.TryGetProperty("enum", out var propEnumNode) && propEnumNode.ValueKind == JsonValueKind.Array)
                    {
                        var ok = false;
                        foreach (var candidate in propEnumNode.EnumerateArray())
                        {
                            if (JsonElementDeepEquals(candidate, value))
                            {
                                ok = true;
                                break;
                            }
                        }

                        if (!ok)
                        {
                            error = $"Property '{prop.Name}': value is not in enum.";
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private static bool IsInstanceTypeAllowed(JsonElement typeNode, JsonElement instance, out string? error)
    {
        error = null;

        if (typeNode.ValueKind == JsonValueKind.String)
        {
            return IsInstanceTypeAllowed(typeNode.GetString(), instance, out error);
        }

        if (typeNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeNode.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (IsInstanceTypeAllowed(item.GetString(), instance, out _))
                {
                    return true;
                }
            }

            error = "Type is not allowed.";
            return false;
        }

        // Unknown type expression, treat as invalid schema.
        error = "Invalid 'type' in schema.";
        return false;
    }

    private static bool IsInstanceTypeAllowed(string? schemaType, JsonElement instance, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(schemaType))
        {
            error = "Invalid 'type' in schema.";
            return false;
        }

        return schemaType switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object || Fail("Expected object", out error),
            "array" => instance.ValueKind == JsonValueKind.Array || Fail("Expected array", out error),
            "string" => instance.ValueKind == JsonValueKind.String || Fail("Expected string", out error),
            "boolean" => instance.ValueKind == JsonValueKind.True || instance.ValueKind == JsonValueKind.False || Fail("Expected boolean", out error),
            "null" => instance.ValueKind == JsonValueKind.Null || Fail("Expected null", out error),
            "number" => instance.ValueKind == JsonValueKind.Number || Fail("Expected number", out error),
            "integer" => (instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _)) || Fail("Expected integer", out error),
            _ => true // Unknown types are treated as permissive in this lite validator.
        };
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    private static bool JsonElementDeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        return a.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquals(a, b),
            JsonValueKind.Array => ArrayEquals(a, b),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText()
        };
    }

    private static bool ObjectEquals(JsonElement a, JsonElement b)
    {
        var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        if (aProps.Count != bProps.Count)
        {
            return false;
        }

        foreach (var (key, aVal) in aProps)
        {
            if (!bProps.TryGetValue(key, out var bVal))
            {
                return false;
            }

            if (!JsonElementDeepEquals(aVal, bVal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArrayEquals(JsonElement a, JsonElement b)
    {
        var aArr = a.EnumerateArray().ToArray();
        var bArr = b.EnumerateArray().ToArray();

        if (aArr.Length != bArr.Length)
        {
            return false;
        }

        for (var i = 0; i < aArr.Length; i++)
        {
            if (!JsonElementDeepEquals(aArr[i], bArr[i]))
            {
                return false;
            }
        }

        return true;
    }
}
