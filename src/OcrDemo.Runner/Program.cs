using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OcrDemo.Engines;
using OcrDemo.Runner;

// Użycie:
//   dotnet run --project src/OcrDemo.Runner -- [--input <katalog>] [--output <katalog>] [--engines ocrmypdf,doctr,...]
// Konfiguracja: appsettings.json, zmienne środowiskowe (np. Engines__Doctr__BaseUrl), argumenty wiersza poleceń.

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    ["--input"] = $"{RunnerOptions.Section}:{nameof(RunnerOptions.InputDirectory)}",
    ["--output"] = $"{RunnerOptions.Section}:{nameof(RunnerOptions.OutputDirectory)}",
    ["--engines"] = $"{RunnerOptions.Section}:{nameof(RunnerOptions.EngineFilter)}",
});

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.Configure<RunnerOptions>(builder.Configuration.GetSection(RunnerOptions.Section));
builder.Services.AddOcrEngines(builder.Configuration);
builder.Services.AddSingleton<BenchmarkRunner>();

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await host.Services.GetRequiredService<BenchmarkRunner>().RunAsync(cts.Token);
