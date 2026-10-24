using System.Text;

namespace LoomX.Assistant.ClientConfig;

/// <summary>客户端配置文件的统一读写细节：UTF-8 无 BOM、时间戳备份命名。</summary>
public static class ClientConfigFiles
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string BackupPathFor(string path) =>
        $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmssfff}";
}
