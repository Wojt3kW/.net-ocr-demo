using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OcrDemo.Core;
using OcrDemo.Engines.Docling;

namespace OcrDemo.Engines;

public static class ServiceCollectionExtensions
{
    /// <summary>Rejestruje włączone w konfiguracji silniki jako <see cref="IOcrEngine"/>.</summary>
    public static IServiceCollection AddOcrEngines(this IServiceCollection services, IConfiguration configuration)
    {
        var ocrMyPdf = Bind<OcrMyPdfOptions>(configuration, EngineSections.OcrMyPdf);
        if (ocrMyPdf.Enabled)
        {
            AddClient(services, "ocrmypdf", ocrMyPdf);
            services.AddSingleton<IOcrEngine>(sp => new OcrMyPdfEngine(Client(sp, "ocrmypdf")));
        }

        var paddle = Bind<PaddleOcrOptions>(configuration, EngineSections.PaddleOcr);
        if (paddle.Enabled)
        {
            AddClient(services, "paddleocr", paddle);
            services.AddSingleton<IOcrEngine>(sp => new SidecarJsonEngine("paddleocr", Client(sp, "paddleocr")));
        }

        var doctr = Bind<DoctrOptions>(configuration, EngineSections.Doctr);
        if (doctr.Enabled)
        {
            AddClient(services, "doctr", doctr);
            services.AddSingleton<IOcrEngine>(sp => new SidecarJsonEngine("doctr", Client(sp, "doctr")));
        }

        var docling = Bind<DoclingOptions>(configuration, EngineSections.Docling);
        if (docling.Enabled)
        {
            AddClient(services, "docling", docling);
            foreach (var preset in docling.OcrPresets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                services.AddSingleton<IOcrEngine>(sp => new DoclingEngine(Client(sp, "docling"), docling, preset));
            }
        }

        return services;
    }

    private static T Bind<T>(IConfiguration configuration, string section) where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private static void AddClient(IServiceCollection services, string name, SidecarEngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException($"Brak BaseUrl dla silnika '{name}' (sekcja {EngineSections.Root}).");
        }

        services.AddHttpClient(name, client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
    }

    private static HttpClient Client(IServiceProvider sp, string name) =>
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(name);
}
