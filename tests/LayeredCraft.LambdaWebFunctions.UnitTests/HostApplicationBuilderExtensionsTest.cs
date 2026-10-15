using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LayeredCraft.LambdaWebFunctions.UnitTests;

public sealed class HostApplicationBuilderExtensionsTest
{
    [Fact]
    public void ConfigureWebFunctionHosting_WhenLambdaVariablesAreAbsent_DoesNotConfigureHost()
    {
        // Arrange
        using var endpoint = new EnvironmentVariableScope("AWS_LAMBDA_HTTP_ENDPOINT", null);
        using var runtimeApi = new EnvironmentVariableScope("AWS_LAMBDA_RUNTIME_API", null);
        var builder = Host.CreateApplicationBuilder();

        // Act
        builder.ConfigureWebFunctionHosting();

        // Assert
        builder.Configuration["urls"].Should().BeNull();
        builder.Services.Should()
            .NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void
        ConfigureWebFunctionHosting_WhenLambdaVariablesArePresent_ConfiguresEndpointAndReadyService()
    {
        // Arrange
        using var endpoint =
            new EnvironmentVariableScope("AWS_LAMBDA_HTTP_ENDPOINT", "127.0.0.1:9000");
        using var runtimeApi =
            new EnvironmentVariableScope("AWS_LAMBDA_RUNTIME_API", "127.0.0.1:9001");
        var builder = Host.CreateApplicationBuilder();

        // Act
        var configured = builder.ConfigureWebFunctionHosting();

        // Assert
        configured.Should().BeSameAs(builder);
        builder.Configuration["urls"].Should().Be("http://127.0.0.1:9000");
        using var services = builder.Services.BuildServiceProvider();
        services.GetServices<IHostedService>().Should().ContainSingle().Which.Should()
            .BeAssignableTo<IHostedLifecycleService>();
    }
}

public sealed class LambdaRuntimeReadyTest
{
    [Fact]
    public async Task StartedAsync_WhenCalled_PostsReady()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK);
        var service = new LambdaRuntimeReady("runtime-api:9001", handler: handler);

        // Act
        await service.StartedAsync(CancellationToken.None);

        // Assert
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should()
            .Be("http://runtime-api:9001/2025-09-01/ready");
        handler.Body.Should().Be("{}");
    }

    [Fact]
    public async Task StartedAsync_WhenRuntimeReturnsFailure_Throws()
    {
        // Arrange
        var service = new LambdaRuntimeReady("runtime-api:9001",
            handler: new StubHandler(HttpStatusCode.BadGateway));

        // Act
        var act = () => service.StartedAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task StartedAsync_WhenHostedServiceStarts_RunsAfterOtherHostedServices()
    {
        // Arrange
        var marker = new StartedMarker();
        var handler = new StubHandler(HttpStatusCode.OK, () => marker.Started.Should().BeTrue());
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(marker);
        builder.Services.AddHostedService<MarkerHostedService>();
        builder.Services.AddHostedService(_ =>
            new LambdaRuntimeReady("runtime-api:9001", handler: handler));
        using var host = builder.Build();

        // Act
        await host.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        marker.Started.Should().BeTrue();
        handler.Request.Should().NotBeNull();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, Action? onSend = null)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            onSend?.Invoke();
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class StartedMarker
    {
        public bool Started { get; set; }
    }

    private sealed class MarkerHostedService(StartedMarker marker) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            marker.Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previous);
    }
}
