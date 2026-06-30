using FatfMonitor.Api.Compliance;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Encodings.Web;

LoadLocalDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Default;
});
builder.Services.Configure<FatfMonitorOptions>(builder.Configuration.GetSection("FatfMonitor"));
builder.Services.AddSingleton<FatfJurisdictionParser>();
builder.Services.AddSingleton<GovUkFatfAdvisoryParser>();
builder.Services.AddSingleton<IFatfSnapshotStore, FileFatfSnapshotStore>();
builder.Services.AddHttpClient<IFatfLlmVerifier, OpenAiFatfLlmVerifier>();
builder.Services.AddHttpClient<IFatfWebSearchProvider, OpenAiFatfWebSearchProvider>();
builder.Services.AddHttpClient<FatfMonitorService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FatfMonitor/1.0; +https://github.com/andyheggs/fatf-monitor)");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = true,
    AutomaticDecompression = System.Net.DecompressionMethods.All
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/session", () => new
{
    service = "FATF Monitor",
    status = "ok",
    checkedAt = DateTimeOffset.UtcNow
});

app.MapGet("/api/compliance/fatf/latest", async (FatfMonitorService monitor, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await monitor.FetchCurrentAsync(cancellationToken));
    }
    catch (FatfMonitorUnavailableException exception)
    {
        return ToUnavailableProblem(exception);
    }
});

app.MapGet("/api/compliance/fatf/jurisdictions", async (FatfMonitorService monitor, CancellationToken cancellationToken) =>
{
    try
    {
        var snapshot = await monitor.FetchCurrentAsync(cancellationToken);
        return Results.Ok(FatfJurisdictionListResponse.FromSnapshot(snapshot));
    }
    catch (FatfMonitorUnavailableException exception)
    {
        return ToUnavailableProblem(exception);
    }
});

app.MapPost("/api/compliance/fatf/check", async (
    HttpRequest request,
    IConfiguration configuration,
    FatfMonitorService monitor,
    CancellationToken cancellationToken) =>
{
    var configuredToken = configuration["FatfMonitor:CheckToken"];
    if (!string.IsNullOrWhiteSpace(configuredToken))
    {
        var suppliedToken = request.Headers.Authorization.ToString();
        if (!string.Equals(suppliedToken, $"Bearer {configuredToken}", StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }
    }

    try
    {
        return Results.Ok(await monitor.CheckAndPersistAsync(cancellationToken));
    }
    catch (FatfMonitorUnavailableException exception)
    {
        return ToUnavailableProblem(exception);
    }
});

app.Run();

static IResult ToUnavailableProblem(FatfMonitorUnavailableException exception)
{
    return Results.Problem(
        title: "FATF monitor source unavailable",
        detail: exception.Message,
        statusCode: StatusCodes.Status503ServiceUnavailable,
        extensions: new Dictionary<string, object?>
        {
            ["sourceUrl"] = exception.SourceUrl.ToString(),
            ["remediation"] = "Set OPENAI_API_KEY or Llm__ApiKey so the monitor can use OpenAI hosted web search. Direct FATF HTTP access is commonly blocked with 403."
        });
}

static void LoadLocalDotEnv()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var envPath = Path.Combine(directory.FullName, ".env");
        if (File.Exists(envPath))
        {
            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            return;
        }

        directory = directory.Parent;
    }
}

public partial class Program;
