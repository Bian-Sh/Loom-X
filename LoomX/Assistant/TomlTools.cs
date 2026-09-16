using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoomX.Assistant.Configuration;

namespace LoomX.Assistant;

/// <summary>
/// TOML 结构化工具：仅解析参数、约束边界并把安全结果写入模型上下文。
/// </summary>
public static class TomlTools
{
    private const int MaxKeyPathSegments = 32;
    private const int MaxOperations = 64;
    private const int MaxPathLength = 4096;
    private const int MaxStringLength = 16384;
    private const int MaxKeySegmentLength = 256;
    private const string SensitiveKeyPlaceholder = "[sensitive]";

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void RegisterAll(ToolRegistry registry, ITomlDocumentService service)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(service);

        registry.Register(new ToolDefinition
        {
            Name = "toml.read",
            Description = "读取 TOML 文件的安全摘要，包括存在性、合法性和顶层键，不返回完整文件文本。",
            ParametersSchema = ReadSchema(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var result = await service.ReadAsync(path, cancellationToken);
                if (!result.IsValid || result.Errors.Count > 0)
                {
                    return Fail("toml_read_failed", "无法读取 TOML 文件。");
                }

                return Ok(new JsonObject
                {
                    ["exists"] = result.Exists,
                    ["is_valid"] = result.IsValid,
                    ["top_level_keys"] = new JsonArray(result.TopLevelKeys
                        .Select(key => SensitiveKeyPolicy.IsSensitivePath([key]) ? SensitiveKeyPlaceholder : key)
                        .Select(key => (JsonNode?)JsonValue.Create(key))
                        .ToArray()),
                });
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "toml.get",
            Description = "按分段键路径读取 TOML 值；敏感路径和值会脱敏。",
            ParametersSchema = KeyPathSchema(includeValue: false),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var keyPath = RequireKeyPath(args);
                var result = await service.GetAsync(path, keyPath, cancellationToken);
                if (result.Errors.Count > 0)
                {
                    return Fail("toml_get_failed", "无法读取 TOML 路径。");
                }

                if (!result.Found)
                {
                    return Ok(new JsonObject { ["found"] = false });
                }

                if (result.Value is null)
                {
                    return Fail("toml_get_failed", "无法读取 TOML 路径。");
                }

                var value = SensitiveKeyPolicy.Redact(result.Value, keyPath.Segments);
                return Ok(new JsonObject
                {
                    ["found"] = true,
                    ["value_type"] = ValueTypeName(value.Kind),
                    ["value"] = ToSafeJsonNode(value, keyPath.Segments),
                });
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "toml.validate",
            Description = "验证 TOML 文件语法并返回安全的定位摘要，不返回文件内容。",
            ParametersSchema = ReadSchema(),
            RiskLevel = ToolRiskLevel.Read,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var result = await service.ValidateAsync(path, cancellationToken);
                return Ok(new JsonObject
                {
                    ["is_valid"] = result.IsValid,
                    ["line"] = result.Line,
                    ["column"] = result.Column,
                    ["error_count"] = result.Errors.Count,
                });
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "toml.set",
            Description = "设置一个 TOML 键路径。输出只包含修改摘要，不回显输入值。",
            ParametersSchema = KeyPathSchema(includeValue: true),
            RiskLevel = ToolRiskLevel.Write,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var keyPath = RequireKeyPath(args);
                var value = RequireTomlValue(args, "value");
                return WriteResult(await service.PatchAsync(
                    path,
                    [new TomlPatchOperation(TomlPatchKind.Set, keyPath, value)],
                    cancellationToken));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "toml.patch",
            Description = "原子应用非空 TOML set/delete 操作列表。输出不回显输入值。",
            ParametersSchema = PatchSchema(),
            RiskLevel = ToolRiskLevel.Write,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var operations = RequireOperations(args);
                return WriteResult(await service.PatchAsync(path, operations, cancellationToken));
            }),
        });

        registry.Register(new ToolDefinition
        {
            Name = "toml.delete",
            Description = "删除一个 TOML 键路径。",
            ParametersSchema = KeyPathSchema(includeValue: false),
            RiskLevel = ToolRiskLevel.Destructive,
            Handler = (args, cancellationToken) => GuardAsync(async () =>
            {
                var path = RequirePath(args);
                var keyPath = RequireKeyPath(args);
                return WriteResult(await service.PatchAsync(
                    path,
                    [new TomlPatchOperation(TomlPatchKind.Delete, keyPath)],
                    cancellationToken));
            }),
        });
    }

    private static JsonNode ReadSchema() => Schema($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": {{MaxPathLength}} }
          },
          "required": ["path"]
        }
        """);

    private static JsonNode KeyPathSchema(bool includeValue)
    {
        var schema = Schema($$"""
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "path": { "type": "string", "minLength": 1, "maxLength": {{MaxPathLength}} },
                "key_path": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": {{MaxKeyPathSegments}},
                  "items": { "type": "string", "minLength": 1, "maxLength": {{MaxKeySegmentLength}} }
                }
              },
              "required": ["path", "key_path"]
            }
            """).AsObject();

        if (includeValue)
        {
            schema["properties"]!["value"] = ValueSchemaReference();
            schema["$defs"] = ValueDefinitions();
            schema["required"]!.AsArray().Add("value");
        }

        return schema;
    }

    private static JsonNode PatchSchema()
    {
        var schema = Schema($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string", "minLength": 1, "maxLength": {{MaxPathLength}} },
            "operations": {
              "type": "array",
              "minItems": 1,
              "maxItems": {{MaxOperations}},
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "op": { "type": "string", "maxLength": 6, "enum": ["set", "delete"] },
                  "key_path": {
                    "type": "array",
                    "minItems": 1,
                    "maxItems": {{MaxKeyPathSegments}},
                    "items": { "type": "string", "minLength": 1, "maxLength": {{MaxKeySegmentLength}} }
                  },
                  "value": { "$ref": "#/$defs/tomlValue" }
                },
                "required": ["op", "key_path"],
                "allOf": [
                  {
                    "if": { "properties": { "op": { "const": "set" } } },
                    "then": { "required": ["value"] }
                  },
                  {
                    "if": { "properties": { "op": { "const": "delete" } } },
                    "then": { "not": { "required": ["value"] } }
                  }
                ]
              }
            }
          },
          "required": ["path", "operations"]
        }
        """).AsObject();
        schema["$defs"] = ValueDefinitions();
        return schema;
    }

    private static JsonObject ValueSchemaReference() => new()
    {
        ["$ref"] = "#/$defs/tomlValue",
    };

    private static JsonObject ValueDefinitions() => new()
    {
        ["tomlValue"] = Schema($$"""
            {
              "anyOf": [
                { "type": "string", "maxLength": {{MaxStringLength}} },
                { "type": "integer" },
                { "type": "number" },
                { "type": "boolean" },
                {
                  "type": "array",
                  "items": { "$ref": "#/$defs/tomlValue" }
                },
                {
                  "type": "object",
                  "propertyNames": { "type": "string", "maxLength": {{MaxStringLength}} },
                  "additionalProperties": { "$ref": "#/$defs/tomlValue" }
                }
              ]
            }
            """),
    };

    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    private static async Task<ToolResult> GuardAsync(Func<Task<ToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            return Fail("invalid_arguments", "工具参数无效。");
        }
        catch (JsonException)
        {
            return Fail("invalid_arguments", "工具参数无效。");
        }
        catch (Exception)
        {
            return Fail("toml_operation_failed", "TOML 操作未完成。");
        }
    }

    private static ToolResult WriteResult(TomlWriteResult result)
    {
        if (!result.Success || result.Errors.Count > 0)
        {
            return Fail("toml_write_failed", "TOML 修改未完成。");
        }

        return Ok(new JsonObject
        {
            ["success"] = true,
            ["changed"] = result.Changed,
            ["backup_created"] = result.BackupPath is not null,
            ["formatting_changed"] = result.FormattingChanged,
        });
    }

    private static ToolResult Ok(JsonObject json) =>
        ToolResult.Ok(json.ToJsonString(OutputJsonOptions));

    private static ToolResult Fail(string code, string message) =>
        ToolResult.Fail(new JsonObject
        {
            ["error"] = code,
            ["message"] = message,
        }.ToJsonString(OutputJsonOptions));

    private static string RequirePath(JsonNode? args) =>
        RequireString(RequireObject(args), "path", MaxPathLength);

    private static TomlPath RequireKeyPath(JsonNode? args) =>
        RequireKeyPath(RequireObject(args));

    private static TomlPath RequireKeyPath(JsonObject args)
    {
        if (!args.TryGetPropertyValue("key_path", out var node) || node is not JsonArray array)
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        if (array.Count is < 1 or > MaxKeyPathSegments)
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        var segments = new string[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            segments[index] = RequireStringValue(array[index], MaxKeySegmentLength);
        }

        return new TomlPath(segments);
    }

    private static IReadOnlyList<TomlPatchOperation> RequireOperations(JsonNode? args)
    {
        var arguments = RequireObject(args);
        if (!arguments.TryGetPropertyValue("operations", out var node) || node is not JsonArray array)
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        if (array.Count is < 1 or > MaxOperations)
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        var operations = new TomlPatchOperation[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject operation)
            {
                throw new ArgumentException("参数无效。", nameof(args));
            }

            var kind = RequireString(operation, "op", 6);
            var keyPath = RequireKeyPath(operation);
            operations[index] = kind switch
            {
                "set" => new TomlPatchOperation(
                    TomlPatchKind.Set,
                    keyPath,
                    RequireTomlValue(operation, "value")),
                "delete" when !operation.ContainsKey("value") =>
                    new TomlPatchOperation(TomlPatchKind.Delete, keyPath),
                _ => throw new ArgumentException("参数无效。", nameof(args)),
            };
        }

        return operations;
    }

    private static TomlValue RequireTomlValue(JsonNode? args, string name) =>
        RequireTomlValue(RequireObject(args), name);

    private static TomlValue RequireTomlValue(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node is null)
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        ValidateStrings(node);
        using var document = JsonDocument.Parse(node.ToJsonString());
        return TomlValue.FromJsonElement(document.RootElement);
    }

    private static void ValidateStrings(JsonNode node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (text.Length > MaxStringLength)
                {
                    throw new ArgumentException("参数无效。", nameof(node));
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is null)
                    {
                        throw new ArgumentException("参数无效。", nameof(node));
                    }

                    ValidateStrings(item);
                }
                break;
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    if (property.Key.Length > MaxStringLength || property.Value is null)
                    {
                        throw new ArgumentException("参数无效。", nameof(node));
                    }

                    ValidateStrings(property.Value);
                }
                break;
        }
    }

    private static JsonObject RequireObject(JsonNode? args) =>
        args as JsonObject ?? throw new ArgumentException("参数无效。", nameof(args));

    private static string RequireString(JsonObject args, string name, int maxLength)
    {
        if (!args.TryGetPropertyValue(name, out var node))
        {
            throw new ArgumentException("参数无效。", nameof(args));
        }

        return RequireStringValue(node, maxLength);
    }

    private static string RequireStringValue(JsonNode? node, int maxLength)
    {
        if (node is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text)
            || text.Length > maxLength)
        {
            throw new ArgumentException("参数无效。", nameof(node));
        }

        return text;
    }

    private static string ValueTypeName(TomlValueKind kind) => kind switch
    {
        TomlValueKind.String => "string",
        TomlValueKind.Integer => "integer",
        TomlValueKind.Float => "float",
        TomlValueKind.Boolean => "boolean",
        TomlValueKind.Array => "array",
        TomlValueKind.Object => "object",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 TOML 值类型。"),
    };

    private static JsonNode ToSafeJsonNode(TomlValue value, IReadOnlyList<string> path) => value.Kind switch
    {
        TomlValueKind.String => JsonValue.Create((string)value.Value),
        TomlValueKind.Integer => JsonValue.Create((long)value.Value),
        TomlValueKind.Float => JsonValue.Create((double)value.Value),
        TomlValueKind.Boolean => JsonValue.Create((bool)value.Value),
        TomlValueKind.Array => new JsonArray(
            ((IReadOnlyList<TomlValue>)value.Value)
                .Select(item => ToSafeJsonNode(item, path))
                .ToArray()),
        TomlValueKind.Object => ToSafeJsonObject(value, path),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "未知 TOML 值类型。"),
    };

    private static JsonObject ToSafeJsonObject(TomlValue value, IReadOnlyList<string> path)
    {
        var json = new JsonObject();
        foreach (var property in (IReadOnlyDictionary<string, TomlValue>)value.Value)
        {
            var childPath = path.Concat([property.Key]).ToArray();
            if (!SensitiveKeyPolicy.IsSensitivePath(childPath))
            {
                json[property.Key] = ToSafeJsonNode(property.Value, childPath);
            }
        }

        return json;
    }
}
