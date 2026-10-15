using System.Text;
using Microsoft.Extensions.Hosting;

namespace LayeredCraft.LambdaWebFunctions;

internal sealed class LambdaRuntimeReady(
    string runtimeApi,
    TimeSpan? timeout = null,
    HttpMessageHandler? handler = null) : IHostedLifecycleService
{
    private const string ReadyPath = "/2025-09-01/ready";

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var disposeHandler = handler is null;
        using var client = new HttpClient(
            handler ?? new SocketsHttpHandler { UseProxy = false }, disposeHandler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"http://{runtimeApi}{ReadyPath}", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
