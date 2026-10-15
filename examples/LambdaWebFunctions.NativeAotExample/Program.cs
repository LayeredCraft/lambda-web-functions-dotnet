using LayeredCraft.LambdaWebFunctions;

var builder = WebApplication.CreateSlimBuilder(args);
builder.ConfigureWebFunctionHosting();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("OK"));

await app.RunAsync();
