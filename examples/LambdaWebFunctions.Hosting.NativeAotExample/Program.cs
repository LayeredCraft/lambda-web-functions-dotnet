using LayeredCraft.LambdaWebFunctions.Hosting;

var builder = WebApplication.CreateSlimBuilder(args);
builder.ConfigureWebFunctionHosting();

var app = builder.Build();
app.MapGet("/health", () => Results.Text("OK"));

await app.RunAsync();
