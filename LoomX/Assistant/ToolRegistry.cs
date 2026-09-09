using System.Text.Json.Nodes;

namespace LoomX.Assistant;

public enum ToolRiskLevel
{
    Read,
    Write,
    External,
    Destructive,
    Secret,
}

/// <summary>
/// 工具执行结果。Content 会进入模型上下文，必须是不含 Secret 的安全内容。
/// </summary>
public sealed record ToolResult(bool Success, string Content)
{
    public static ToolResult Ok(string content) => new(true, content);

    public static ToolResult Fail(string error) => new(false, error);
}

/// <summary>
/// 工具定义：名称、描述、JSON Schema、处理器、超时与风险等级。
/// </summary>
public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonNode ParametersSchema { get; init; }
    public required Func<JsonNode?, CancellationToken, Task<ToolResult>> Handler { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Read;
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> tools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ToolDefinition> All => tools.Values.ToArray();

    public void Register(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrWhiteSpace(tool.Name)) throw new ArgumentException("工具名称不能为空。", nameof(tool));
        if (!tools.TryAdd(tool.Name, tool)) throw new InvalidOperationException($"工具 '{tool.Name}' 已注册。");
    }

    public bool TryGet(string name, out ToolDefinition? tool) => tools.TryGetValue(name, out tool);
}
