# AGENTS.md

This package configures ASP.NET Core for AWS Lambda Web Functions.

- Keep the public API to `ConfigureWebFunctionHosting` unless a second real user setting appears.
- Preserve NativeAOT and trimming compatibility; do not introduce reflection-based DI or JSON.
- Keep the ready signal after Kestrel starts and bypass proxy configuration for the runtime API.
- Run `dotnet build`, `dotnet test`, and `task format:cleanupcode` for code changes.
- Use conventional commits and keep CI workflows pinned to one `devops-templates` version.
