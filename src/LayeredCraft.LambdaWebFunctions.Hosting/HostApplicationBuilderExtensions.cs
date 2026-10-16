using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LayeredCraft.LambdaWebFunctions.Hosting;

/// <summary>
/// Configures a host to run as an AWS Lambda Web Function.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <param name="builder">The host builder to configure.</param>
    /// <typeparam name="TBuilder">The concrete host builder type.</typeparam>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        /// Binds the host to Lambda's HTTP endpoint and signals ready after it starts listening.
        /// </summary>
        /// <returns><paramref name="builder" />.</returns>
        public TBuilder ConfigureWebFunctionHosting()
        {
            ArgumentNullException.ThrowIfNull(builder);

            var endpoint = builder.Configuration["AWS_LAMBDA_HTTP_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(endpoint))
                builder.Configuration["urls"] = $"http://{endpoint}";

            var runtimeApi = builder.Configuration["AWS_LAMBDA_RUNTIME_API"];
            if (!string.IsNullOrWhiteSpace(runtimeApi))
                builder.Services.AddHostedService(_ => new LambdaRuntimeReady(runtimeApi));

            return builder;
        }
    }
}
