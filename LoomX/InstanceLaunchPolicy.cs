namespace OllamaHub.Desktop;

internal static class InstanceLaunchPolicy
{
    public const string AllowMultipleInstancesArgument = "--allow-multiple-instances";

    public static bool AllowsMultipleInstances(IReadOnlyList<string>? arguments, string? environmentValue)
    {
        if (string.Equals(environmentValue, "1", StringComparison.Ordinal))
            return true;

        return arguments?.Skip(1).Any(argument =>
            string.Equals(argument, AllowMultipleInstancesArgument, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
