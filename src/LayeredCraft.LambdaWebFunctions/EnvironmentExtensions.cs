using System.Diagnostics.CodeAnalysis;

namespace LayeredCraft.LambdaWebFunctions;

internal static class EnvironmentExtensions
{
    extension(Environment)
    {
        internal static bool TryGetEnvironmentVariable(
            string name,
            [NotNullWhen(true)] out string? value)
        {
            value = Environment.GetEnvironmentVariable(name);
            return value is { Length: > 0 };
        }
    }
}
