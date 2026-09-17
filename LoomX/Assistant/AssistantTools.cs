using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Assistant.UserDecisions;

namespace LoomX.Assistant;

/// <summary>Assistant 自身的交互工具。</summary>
public static class AssistantTools
{
    private static readonly AsyncLocal<string?> CurrentOwnerId = new();
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> RequestProperties = new(StringComparer.Ordinal)
    {
        "title", "question", "reason", "impact_summary", "allow_cancel", "fields",
    };
    private static readonly HashSet<string> FieldProperties = new(StringComparer.Ordinal)
    {
        "id", "label", "type", "is_required", "options", "default_option_id", "default_option_ids",
        "min_selections", "max_selections", "default_number", "min_number", "max_number", "step",
        "default_text", "is_multiline", "max_length",
    };
    private static readonly HashSet<string> OptionProperties = new(StringComparer.Ordinal)
    {
        "id", "label", "description",
    };

    public static void RegisterAll(ToolRegistry registry, IUserDecisionBroker broker)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(broker);

        registry.Register(new ToolDefinition
        {
            Name = "assistant.ask_user",
            Description = "暂停当前步骤并向用户收集一组结构化业务决策；不得用于索取密钥或认证信息。",
            ParametersSchema = CreateAskUserSchema(),
            RiskLevel = ToolRiskLevel.Read,
            SafeArgumentsProjector = CreateSafeArgumentsProjection,
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            Handler = async (arguments, cancellationToken) =>
            {
                try
                {
                    var request = ParseRequest(arguments);
                    var ownerId = CurrentOwnerId.Value ?? Guid.NewGuid().ToString("N");
                    var result = await broker.RequestAsync(ownerId, request, cancellationToken).ConfigureAwait(false);
                    return ToolResult.Ok(SerializeResult(request, result));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UserDecisionValidationException)
                {
                    return Fail("invalid_request", "用户决策请求无效。");
                }
                catch (Exception)
                {
                    return Fail("ask_user_failed", "无法处理用户决策请求。");
                }
            },
        });
    }

    internal static IDisposable BeginRun(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var previous = CurrentOwnerId.Value;
        CurrentOwnerId.Value = ownerId;
        return new OwnerScope(previous);
    }


    private static JsonNode CreateSafeArgumentsProjection(JsonNode? arguments)
    {
        var root = arguments as JsonObject ?? throw new ArgumentException("工具参数必须是对象。", nameof(arguments));
        var fields = root["fields"] as JsonArray;
        var fieldSummaries = new JsonArray();
        if (fields is not null)
        {
            foreach (var item in fields)
            {
                var field = item as JsonObject;
                var type = field?["type"] is JsonValue typeValue
                    && typeValue.TryGetValue<string>(out var rawType)
                    && rawType is "single_select" or "multi_select" or "number" or "text"
                        ? rawType
                        : "unknown";
                var required = field?["is_required"] is JsonValue requiredValue
                    && requiredValue.TryGetValue<bool>(out var isRequired)
                    && isRequired;
                fieldSummaries.Add(new JsonObject
                {
                    ["type"] = type,
                    ["required"] = required,
                    ["option_count"] = field?["options"] is JsonArray options ? options.Count : 0,
                });
            }
        }

        return new JsonObject
        {
            ["field_count"] = fields?.Count ?? 0,
            ["fields"] = fieldSummaries,
            ["allow_cancel"] = root["allow_cancel"] is JsonValue allowCancelValue
                && allowCancelValue.TryGetValue<bool>(out var allowCancel)
                ? allowCancel
                : true,
        };
    }

    private static UserDecisionRequest ParseRequest(JsonNode? arguments)
    {
        var root = arguments as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "工具参数必须是对象。")]);
        ValidateProperties(root, RequestProperties);
        var fieldsNode = root["fields"] as JsonArray
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);

        var fields = fieldsNode.Select(ParseField).ToArray();
        var request = new UserDecisionRequest(
            RequireString(root, "title"),
            RequireString(root, "question"),
            fields,
            OptionalString(root, "reason"),
            OptionalString(root, "impact_summary"),
            OptionalValue(root, "allow_cancel", true));
        ValidateContentBoundary(request);
        return request;
    }

    private static UserDecisionField ParseField(JsonNode? node)
    {
        var field = node as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);
        ValidateProperties(field, FieldProperties);
        var type = ParseFieldType(RequireString(field, "type"));
        var options = ReadOptions(field, "options");

        return new UserDecisionField(
            RequireString(field, "id"),
            RequireString(field, "label"),
            type,
            OptionalValue(field, "is_required", false),
            options,
            OptionalString(field, "default_option_id"),
            ReadStringArray(field, "default_option_ids"),
            OptionalNullableValue<int>(field, "min_selections"),
            OptionalNullableValue<int>(field, "max_selections"),
            OptionalNullableValue<decimal>(field, "default_number"),
            OptionalNullableValue<decimal>(field, "min_number"),
            OptionalNullableValue<decimal>(field, "max_number"),
            OptionalNullableValue<decimal>(field, "step"),
            OptionalString(field, "default_text"),
            OptionalValue(field, "is_multiline", false),
            OptionalNullableValue<int>(field, "max_length"));
    }

    private static UserDecisionOption ParseOption(JsonNode? node)
    {
        var option = node as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策选项无效。")]);
        ValidateProperties(option, OptionProperties);
        return new UserDecisionOption(
            RequireString(option, "id"),
            RequireString(option, "label"),
            OptionalString(option, "description"));
    }

    private static UserDecisionFieldType ParseFieldType(string value) => value switch
    {
        "single_select" => UserDecisionFieldType.SingleSelect,
        "multi_select" => UserDecisionFieldType.MultiSelect,
        "number" => UserDecisionFieldType.Number,
        "text" => UserDecisionFieldType.Text,
        _ => throw new UserDecisionValidationException(
            [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段类型无效。")]),
    };

    private static void ValidateProperties(JsonObject source, IReadOnlySet<string> allowedProperties)
    {
        if (source.Any(property => !allowedProperties.Contains(property.Key)))
        {
            throw InvalidRequest("用户决策请求包含未知属性。");
        }
    }

    private static void ValidateContentBoundary(UserDecisionRequest request)
    {
        if (EnumerateDisplayContent(request).Any(IsProhibitedContent))
        {
            throw InvalidRequest("用户决策请求包含禁止的正文或配置内容。");
        }
    }

    private static IEnumerable<string?> EnumerateDisplayContent(UserDecisionRequest request)
    {
        yield return request.Title;
        yield return request.Question;
        yield return request.Description;
        yield return request.ImpactSummary;

        foreach (var field in request.Fields)
        {
            yield return field.Label;
            yield return field.DefaultText;
            foreach (var option in field.Options)
            {
                yield return option.Label;
                yield return option.Description;
            }
        }
    }

    private static bool IsProhibitedContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.Trim();
        if (LooksLikeJsonDocument(trimmed))
        {
            return true;
        }

        var lines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assignmentCount = lines.Count(LooksLikeTomlAssignment);
        if (assignmentCount >= 2 || (assignmentCount >= 1 && lines.Any(LooksLikeTomlTableHeader)))
        {
            return true;
        }

        return lines.Any(LooksLikeHttpHeader);
    }

    private static bool LooksLikeJsonDocument(string content)
    {
        if (content.Length < 2
            || (content[0] != '{' || content[^1] != '}')
            && (content[0] != '[' || content[^1] != ']'))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(content) is JsonObject or JsonArray;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeTomlTableHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static bool LooksLikeTomlAssignment(string line)
    {
        var trimmed = line.Trim();
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex == trimmed.Length - 1)
        {
            return false;
        }

        var key = trimmed[..equalsIndex].Trim();
        if (key.Length >= 2
            && (key[0] == '"' && key[^1] == '"' || key[0] == '\'' && key[^1] == '\''))
        {
            return true;
        }

        return key.All(character => char.IsLetterOrDigit(character)
            || character is '_' or '-' or '.' or ' ');
    }

    private static bool LooksLikeHttpHeader(string line)
    {
        var trimmed = line.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == trimmed.Length - 1)
        {
            return false;
        }

        var name = trimmed[..colonIndex];
        return name.All(character => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.'
            or '^' or '_' or '`' or '|' or '~');
    }

    private static UserDecisionValidationException InvalidRequest(string message) =>
        new([new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, message)]);

    private static string RequireString(JsonObject source, string propertyName)
    {
        if (source[propertyName] is JsonValue value && value.TryGetValue<string>(out var result))
        {
            return result;
        }

        throw InvalidRequest("用户决策请求缺少必填属性或属性类型无效。");
    }

    private static string? OptionalString(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : throw InvalidRequest("用户决策可选属性类型无效。");
    }

    private static T OptionalValue<T>(JsonObject source, string propertyName, T defaultValue)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return defaultValue;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return ReadValue<T>(node);
    }

    private static T? OptionalNullableValue<T>(JsonObject source, string propertyName)
        where T : struct
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return ReadValue<T>(node);
    }

    private static T ReadValue<T>(JsonNode node)
    {
        if (node is not JsonValue)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        try
        {
            return node.Deserialize<T>()!;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }
    }

    private static IReadOnlyList<UserDecisionOption> ReadOptions(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return [];
        }

        if (node is not JsonArray items)
        {
            throw InvalidRequest("用户决策选项类型无效。");
        }

        return items.Select(ParseOption).ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return [];
        }

        if (node is not JsonArray items)
        {
            throw InvalidRequest("用户决策默认选项类型无效。");
        }

        return items.Select(item => item is JsonValue value && value.TryGetValue<string>(out var result)
                ? result
                : throw InvalidRequest("用户决策默认选项无效。"))
            .ToArray();
    }

    private static ToolResult Fail(string code, string message) =>
        ToolResult.SafeFail(new JsonObject
        {
            ["error"] = code,
            ["message"] = message,
        }.ToJsonString(OutputJsonOptions));

    private static string SerializeResult(UserDecisionRequest request, UserDecisionResult result)
    {
        var fields = request.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        var values = new JsonObject();
        foreach (var pair in result.Values)
        {
            values[pair.Key] = fields[pair.Key].Type == UserDecisionFieldType.Text
                ? new JsonObject { ["provided"] = pair.Value is not null }
                : JsonSerializer.SerializeToNode(pair.Value, OutputJsonOptions);
        }

        return new JsonObject
        {
            ["cancelled"] = result.Cancelled,
            ["values"] = values,
        }.ToJsonString(OutputJsonOptions);
    }

    private static JsonNode CreateAskUserSchema() => JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "question", "fields"],
          "properties": {
            "title": { "type": "string", "maxLength": 120 },
            "question": { "type": "string", "maxLength": 2000 },
            "reason": { "type": "string", "maxLength": 2000 },
            "impact_summary": { "type": "string", "maxLength": 1000 },
            "allow_cancel": { "type": "boolean", "default": true },
            "fields": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "label", "type"],
                "properties": {
                  "id": { "type": "string", "maxLength": 64 },
                  "label": { "type": "string", "maxLength": 200 },
                  "type": { "type": "string", "enum": ["single_select", "multi_select", "number", "text"] },
                  "is_required": { "type": "boolean", "default": false },
                  "options": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["id", "label"],
                      "properties": {
                        "id": { "type": "string", "maxLength": 64 },
                        "label": { "type": "string", "maxLength": 200 },
                        "description": { "type": "string", "maxLength": 500 }
                      }
                    }
                  },
                  "default_option_id": { "type": "string" },
                  "default_option_ids": { "type": "array", "items": { "type": "string" } },
                  "min_selections": { "type": "integer", "minimum": 0 },
                  "max_selections": { "type": "integer", "minimum": 0 },
                  "default_number": { "type": "number" },
                  "min_number": { "type": "number" },
                  "max_number": { "type": "number" },
                  "step": { "type": "number", "exclusiveMinimum": 0 },
                  "default_text": { "type": "string" },
                  "is_multiline": { "type": "boolean", "default": false },
                  "max_length": { "type": "integer", "minimum": 1, "maximum": 4000 }
                }
              }
            }
          }
        }
        """)!;

    private sealed class OwnerScope(string? previousOwnerId) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CurrentOwnerId.Value = previousOwnerId;
        }
    }
}
