using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LayeredCraft.LambdaWebFunctions;

/// <summary>
/// Configures a host to run as an AWS Lambda Web Function.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Binds the host to Lambda's HTTP endpoint and signals ready after it starts listening.
    /// </summary>
    /// <typeparam name="TBuilder">The concrete host builder type.</typeparam>
    /// <param name="builder">The host builder to configure.</param>
    /// <returns><paramref name="builder" />.</returns>
    public static TBuilder ConfigureWebFunctionHosting<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoint = Environment.GetEnvironmentVariable("AWS_LAMBDA_HTTP_ENDPOINT");
        if (endpoint is { Length: > 0 })
            builder.Configuration["urls"] = $"http://{endpoint}";

        var runtimeApi = Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API");
        if (runtimeApi is { Length: > 0 })
            builder.Services.AddHostedService(_ => new LambdaRuntimeReady(runtimeApi));

        return builder;
    }
}
