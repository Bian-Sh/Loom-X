namespace LoomX.Services;

/// <summary>统一生成真实路由与 Provider 测试使用的上游地址，避免两条执行路径出现不同的 URL 语义。</summary>
internal static class ProviderRouteEndpointResolver
{
    public static Uri Resolve(string baseUrl, string path, string? queryString = null)
    {
        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return new Uri(normalizedBaseUrl + normalizedPath + queryString, UriKind.Absolute);
    }

    public static bool HasRepeatedVersionSegment(Uri? endpoint)
    {
        if (endpoint is null) return false;
        var segments = endpoint.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 1; index < segments.Length; index++)
        {
            if (IsVersionSegment(segments[index])
                && string.Equals(segments[index - 1], segments[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVersionSegment(string segment) =>
        segment.Length > 1
        && segment[0] is 'v' or 'V'
        && segment.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
}
