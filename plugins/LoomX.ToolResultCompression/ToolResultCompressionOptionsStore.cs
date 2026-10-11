namespace LoomX.ToolResultCompression;

/// <summary>
/// 插件自有配置存储。配置落在插件 DataDirectory，不触碰宿主核心配置。
/// 读取失败一律回退默认配置，不让配置问题影响 Router 主流程。
/// </summary>
public sealed class ToolResultCompressionOptionsStore(string dataDirectory)
{
    private const string ConfigFileName = "config.json";

    private readonly object gate = new();
    private readonly string configPath = Path.Combine(dataDirectory, ConfigFileName);
    private ToolResultCompressionOptions current = ToolResultCompressionOptions.Default;

    public ToolResultCompressionOptions Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    public string ConfigPath => configPath;

    /// <summary>加载配置；文件缺失时写入默认配置，写入失败只回退内存默认值。</summary>
    public ToolResultCompressionOptions Load()
    {
        lock (gate)
        {
            if (File.Exists(configPath))
            {
                string json;
                try
                {
                    json = File.ReadAllText(configPath);
                }
                catch (IOException)
                {
                    return current;
                }
                catch (UnauthorizedAccessException)
                {
                    return current;
                }

                current = ToolResultCompressionOptions.FromJson(json) ?? ToolResultCompressionOptions.Default;
            }
            else
            {
                WriteDefault();
            }

            return current;
        }
    }

    /// <summary>保存配置；失败时保留内存中的当前配置。</summary>
    public ToolResultCompressionOptions Save(ToolResultCompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (gate)
        {
            current = options.Normalized();
            try
            {
                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(configPath, current.ToJson());
            }
            catch (IOException)
            {
                // 配置写入失败不影响本次运行：内存中的配置已经生效。
            }
            catch (UnauthorizedAccessException)
            {
            }

            return current;
        }
    }

    private void WriteDefault()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(configPath, ToolResultCompressionOptions.Default.ToJson());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
