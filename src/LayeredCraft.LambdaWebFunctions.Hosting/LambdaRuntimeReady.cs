using System.Text;
using Microsoft.Extensions.Hosting;

namespace LayeredCraft.LambdaWebFunctions.Hosting;

internal sealed class LambdaRuntimeReady(string runtimeApi, HttpMessageHandler? handler = null)
    : IHostedLifecycleService
{
    private const string ReadyPath = "/2025-09-01/ready";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var disposeHandler = handler is null;

        // ReSharper disable once ShortLivedHttpClient
        using var client = new HttpClient(
            handler ?? new SocketsHttpHandler { UseProxy = false },
            disposeHandler);
        client.Timeout = ReadyTimeout;

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response =
            await client
                .PostAsync($"http://{runtimeApi}{ReadyPath}", content, cancellationToken)
                .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }
}
