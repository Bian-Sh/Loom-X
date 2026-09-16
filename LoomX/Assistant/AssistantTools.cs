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
            Handler = async (arguments, cancellationToken) =>
            {
                try
                {
                    var request = ParseRequest(arguments);
                    var ownerId = CurrentOwnerId.Value ?? Guid.NewGuid().ToString("N");
                    var result = await broker.RequestAsync(ownerId, request, cancellationToken).ConfigureAwait(false);
                    return ToolResult.Ok(SerializeResult(result));
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

    private static UserDecisionRequest ParseRequest(JsonNode? arguments)
    {
        var root = arguments as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "工具参数必须是对象。")]);
        var fieldsNode = root["fields"] as JsonArray
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);

        var fields = fieldsNode.Select(ParseField).ToArray();
        return new UserDecisionRequest(
            RequireString(root, "title"),
            RequireString(root, "question"),
            fields,
            OptionalString(root, "reason"),
            OptionalString(root, "impact_summary"),
            OptionalValue(root, "allow_cancel", true));
    }

    private static UserDecisionField ParseField(JsonNode? node)
    {
        var field = node as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);
        var type = ParseFieldType(RequireString(field, "type"));
        var options = (field["options"] as JsonArray)?.Select(ParseOption).ToArray()
            ?? Array.Empty<UserDecisionOption>();

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

    private static string RequireString(JsonObject source, string propertyName) =>
        source[propertyName]?.GetValue<string>()
        ?? throw new UserDecisionValidationException(
            [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策请求缺少必填属性。")]);

    private static string? OptionalString(JsonObject source, string propertyName) =>
        source[propertyName]?.GetValue<string>();

    private static T OptionalValue<T>(JsonObject source, string propertyName, T defaultValue) =>
        source[propertyName] is { } node ? node.Deserialize<T>()! : defaultValue;

    private static T? OptionalNullableValue<T>(JsonObject source, string propertyName)
        where T : struct => source[propertyName] is { } node ? node.Deserialize<T>() : null;

    private static IReadOnlyList<string> ReadStringArray(JsonObject source, string propertyName) =>
        source[propertyName] is JsonArray items
            ? items.Select(item => item?.GetValue<string>()
                ?? throw new UserDecisionValidationException(
                    [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策默认选项无效。")]))
                .ToArray()
            : Array.Empty<string>();

    private static ToolResult Fail(string code, string message) =>
        ToolResult.Fail(new JsonObject
        {
            ["error"] = code,
            ["message"] = message,
        }.ToJsonString(OutputJsonOptions));

    private static string SerializeResult(UserDecisionResult result)
    {
        var values = new JsonObject();
        foreach (var pair in result.Values)
        {
            values[pair.Key] = JsonSerializer.SerializeToNode(pair.Value, OutputJsonOptions);
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
