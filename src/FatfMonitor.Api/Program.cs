using FatfMonitor.Api.Compliance;

LoadLocalDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<FatfMonitorOptions>(builder.Configuration.GetSection("FatfMonitor"));
builder.Services.AddSingleton<FatfJurisdictionParser>();
builder.Services.AddSingleton<IFatfSnapshotStore, FileFatfSnapshotStore>();
builder.Services.AddHttpClient<IFatfLlmVerifier, OpenAiFatfLlmVerifier>();
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
    Results.Ok(await monitor.FetchCurrentAsync(cancellationToken)));

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

    return Results.Ok(await monitor.CheckAndPersistAsync(cancellationToken));
});

app.Run();

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
