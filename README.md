# LayeredCraft.LambdaWebFunctions

`LayeredCraft.LambdaWebFunctions` configures an ASP.NET Core host for an AWS Lambda Web Function.
It does two things when Lambda supplies its Web Function environment variables:

1. Configures Kestrel to listen on `AWS_LAMBDA_HTTP_ENDPOINT`.
2. Signals `/2025-09-01/ready` to Lambda after hosted services, including Kestrel, have started.

It is a small hosting adapter. It does not add routing, middleware, serialization, or an AWS SDK
dependency.

## Why this package exists

Lambda Web Functions currently start a Node.js runtime. A .NET app can run there by shipping an
`index.js` entrypoint that replaces Node with a native .NET executable through `process.execve`.
That replacement also removes the Lambda runtime client that normally reports readiness. Without a
replacement ready signal, Lambda initialization times out even if Kestrel is listening.

This package restores that missing part of the contract while keeping an ASP.NET Core app normal:
it listens on the address Lambda chose and sends ready only after the host is started.

## Requirements

- .NET 9, 10, or 11
- An AWS Lambda Web Function using the `nodejs24.x` runtime
- A deployment package containing an `index.js` shim and your published .NET executable

The package has no effect during ordinary local development because Lambda does not set the two
environment variables it reads.

## Install

```bash
dotnet add package LayeredCraft.LambdaWebFunctions
```

## Use

Call `ConfigureWebFunctionHosting` immediately after creating the application builder:

```csharp
using LayeredCraft.LambdaWebFunctions;

var builder = WebApplication.CreateSlimBuilder(args);
builder.ConfigureWebFunctionHosting();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok());

await app.RunAsync();
```

The extension returns the same concrete builder, so chaining also works:

```csharp
var builder = WebApplication.CreateSlimBuilder(args)
    .ConfigureWebFunctionHosting();
```

The API targets `IHostApplicationBuilder`, not `WebApplicationBuilder`. It also works with worker
hosts and does not require the package itself to reference `Microsoft.AspNetCore.App`.

## Lambda behavior

When `AWS_LAMBDA_HTTP_ENDPOINT` is non-empty, the extension sets the host's `urls` configuration
value to `http://<endpoint>`. ASP.NET Core then binds Kestrel to Lambda's host and port.

When `AWS_LAMBDA_RUNTIME_API` is non-empty, the extension registers an internal lifecycle hosted
service. Its `StartedAsync` method sends this request:

```text
POST http://$AWS_LAMBDA_RUNTIME_API/2025-09-01/ready
Content-Type: application/json

{}
```

`StartedAsync` runs after all `IHostedService.StartAsync` calls, so Kestrel has already bound. A
failed request or non-success response aborts host startup. The request uses a five-second timeout
and bypasses proxy settings because Lambda's runtime API is local to the execution environment.

The registration is idempotent. Calling `ConfigureWebFunctionHosting` more than once does not add
multiple ready services.

## Deployment shim

Place this `index.js` beside the native executable named `server` in the root of the deployment
zip:

```js
process.execve("/var/task/server", ["/var/task/server"], process.env);
```

The Web Function runtime loads `index.js`; the shim replaces it with the native executable. The
application then receives the Lambda environment variables and this package completes the ready
signal.

## NativeAOT

The library enables .NET trim and NativeAOT analyzers. It avoids reflection-based service creation
and JSON serialization. A CI smoke app publishes the package with `PublishAot=true` for Linux.

A typical publish command for the executable is:

```bash
dotnet publish --configuration Release --runtime linux-arm64 --self-contained true \
  -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -o publish
```

Copy the resulting executable to the deployment zip as `server`, add the shim as `index.js`, and
include any application configuration files your app needs.

## Limits

- Do not configure explicit Kestrel `Listen(...)` endpoints in the same application. Explicit
  listeners override the `urls` value and can prevent Lambda from reaching the application.
- The runtime API address and ready timeout are intentionally not public options. They are supplied
  by Lambda and the fixed timeout bounds failed startup.
- This package supports the current Node `execve` deployment pattern only. It does not implement a
  future `provided.*` runtime.

## Local development

Run the application normally:

```bash
dotnet run
```

With neither Lambda environment variable set, the extension leaves the host untouched and performs
no ready request. You can set the variables explicitly to reproduce the binding behavior locally.

## Development

```bash
dotnet restore
dotnet build
dotnet test
task format:cleanupcode
```

Configure the local pre-commit hook once:

```bash
git config core.hooksPath .githooks
```

## License

MIT. See [LICENSE](LICENSE).
