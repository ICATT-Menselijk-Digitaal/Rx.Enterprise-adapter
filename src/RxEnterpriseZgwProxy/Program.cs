using DotNetEnv;
using RxEnterprise.Client;
using RxEnterpriseZgwProxy.Auth;
using RxEnterpriseZgwProxy.Zgw;
using System.Text.Json.Nodes;

// Try project source directory first (dotnet run), then binary directory (published)
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (!File.Exists(envPath))
    envPath = Path.Combine(AppContext.BaseDirectory, ".env");
Env.Load(envPath);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddZgwAuth(builder.Configuration);

builder.Services.AddRxEnterpriseClient(o =>
{
    o.BaseUrl = Require(builder.Configuration, "RxEnterprise:BaseUrl");
    o.CertificatePath = Require(builder.Configuration, "RxEnterprise:CertificatePath");
    o.PrivateKeyPath = Require(builder.Configuration, "RxEnterprise:PrivateKeyPath");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz").AllowAnonymous();

// ── Zaken ──────────────────────────────────────────────────────────────────

// Search zaken — maps ?identificatie= to Rx.Enterprise search.
// Role-based queries (BSN, vestiging, NNP) are not supported by Rx.Enterprise
// and return an empty result so KISS degrades gracefully.
app.MapGet("/zaken/api/v1/zaken", async (
    HttpRequest request,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";

    if (request.Query.Any(kv => kv.Key.StartsWith("rol__")))
        return Results.Json(ZgwMapper.ToPaginatedResult([]));

    var query = request.Query["identificatie"].FirstOrDefault() ?? string.Empty;
    var raw = await rxClient.SearchZaakAsync(query, ct);
    var rxJson = JsonNode.Parse(raw);

    var zaken = ZgwMapper.Unwrap(rxJson)
        .Select(item =>
        {
            var id = ZgwMapper.ExtractId(item);
            var selfUrl = $"{baseUrl}/zaken/api/v1/zaken/{id}";
            return ZgwMapper.ToZgwZaak(item, selfUrl, baseUrl);
        })
        .ToArray();

    return Results.Json(ZgwMapper.ToPaginatedResult(zaken));
});

// Get single zaak by Rx.Enterprise ID
app.MapGet("/zaken/api/v1/zaken/{id}", async (
    string id,
    HttpRequest request,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";
    var selfUrl = $"{baseUrl}/zaken/api/v1/zaken/{id}";
    var raw = await rxClient.GetZaakAsync(id, ct);
    var rxJson = JsonNode.Parse(raw);
    return Results.Json(ZgwMapper.ToZgwZaak(rxJson, selfUrl, baseUrl));
});

// Rollen — Rx.Enterprise has no ZGW role return empty
app.MapGet("/zaken/api/v1/rollen", () =>
    Results.Json(ZgwMapper.ToPaginatedResult([])));

app.MapGet("/zaken/api/v1/statussen", () =>
    Results.Json(ZgwMapper.ToPaginatedResult([])));

app.MapGet("/zaken/api/v1/statussen/{id}", (
    string id,
    HttpRequest request) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";
    var statusName = ZgwMapper.DecodeBase64Segment(id);
    var selfUrl = $"{baseUrl}/zaken/api/v1/statussen/{id}";

    return Results.Json(ZgwMapper.ToZgwStatus(statusName, selfUrl, baseUrl));
});

app.MapGet("/api/zaken/statussen/{id}", (
    string id,
    HttpRequest request) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";
    var statusName = ZgwMapper.DecodeBase64Segment(id);
    var selfUrl = $"{baseUrl}/api/zaken/statussen/{id}";

    return Results.Json(ZgwMapper.ToZgwStatus(statusName, selfUrl, baseUrl));
});

// Zaakinformatieobjecten — two-step: get zaak → extract documentnummer → get document → map bijlageinfo
app.MapGet("/zaken/api/v1/zaakinformatieobjecten", async (
    HttpRequest request,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var baseUrl = $"{request.Scheme}://{request.Host}";
    var zaakUrl = request.Query["zaak"].FirstOrDefault() ?? string.Empty;
    var zaakId = zaakUrl.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

    if (string.IsNullOrEmpty(zaakId))
        return Results.Json(new JsonArray());

    var zaakRaw = await rxClient.GetZaakAsync(zaakId, ct);
    var zaakNode = JsonNode.Parse(zaakRaw);
    var zaak = ZgwMapper.Unwrap(zaakNode).FirstOrDefault() ?? zaakNode;

    var documentNummer = ZgwMapper.ExtractDocumentNummer(zaak);
    if (string.IsNullOrEmpty(documentNummer))
        return Results.Json(new JsonArray());

    var docRaw = await rxClient.GetDocumentAsync(documentNummer, ct);
    var docNode = JsonNode.Parse(docRaw);

    return Results.Json(ZgwMapper.ToZaakInformatieObjecten(docNode, zaakUrl, baseUrl));
});


// No-op: KISS posts zaakcontactmomenten but Rx.Enterprise has no equivalent
app.MapPost("/zaken/api/v1/zaakcontactmomenten", () => Results.NoContent());

// ── Catalogi ───────────────────────────────────────────────────────────────

// Zaaktype — the ID segment is a base64-encoded type name written by ZgwMapper
app.MapGet("/catalogi/api/v1/zaaktypen/{id}", (string id) =>
{
    var name = ZgwMapper.DecodeBase64Segment(id);
    return Results.Json(new
    {
        onderwerp = name,
        omschrijving = name,
        doorlooptijd = (string?)null,
        servicenorm = (string?)null,
    });
});

app.MapGet("/catalogi/api/v1/statustypen/{id}", (string id) =>
{
    var name = ZgwMapper.DecodeBase64Segment(id);
    return Results.Json(new { omschrijving = name });
});

// ── Documenten ─────────────────────────────────────────────────────────────

// Get document metadata by Rx.Enterprise document ID (e.g. D2025-01-000009)
app.MapGet("/documenten/api/v1/documenten/{id}", async (
    string id,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var raw = await rxClient.GetDocumentAsync(id, ct);
    return Results.Json(JsonNode.Parse(raw));
});

// Download document binary — id is the document ID, filename is the file name with extension
app.MapGet("/documenten/api/v1/documenten/{id}/{filename}", async (
    string id,
    string filename,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var (stream, contentType, resolvedName) = await rxClient.DownloadDocumentAsync(id, filename, ct);
    return Results.Stream(stream, contentType, resolvedName);
});

// ZGW enkelvoudiginformatieobjecten — id is "{documentNummer}--{virtualId}" (e.g. D2025-01-000009--1)
app.MapGet("/documenten/api/v1/enkelvoudiginformatieobjecten/{id}", async (
    string id,
    HttpRequest request,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var parts = id.Split("--");
    if (parts.Length != 2 || !int.TryParse(parts[1], out var virtualId))
        return Results.NotFound();

    var baseUrl = $"{request.Scheme}://{request.Host}";
    var selfUrl = $"{baseUrl}/documenten/api/v1/enkelvoudiginformatieobjecten/{Uri.EscapeDataString(id)}";

    var raw = await rxClient.GetDocumentAsync(parts[0], ct);
    var docNode = JsonNode.Parse(raw);
    return Results.Json(ZgwMapper.ToEnkelvoudigInformatieObject(docNode, virtualId, selfUrl));
});

// Download attachment by compound id — resolves filename from document bijlageinfo then streams
app.MapGet("/documenten/api/v1/enkelvoudiginformatieobjecten/{id}/download", async (
    string id,
    IRxEnterpriseClient rxClient,
    CancellationToken ct) =>
{
    var parts = id.Split("--");
    if (parts.Length != 2 || !int.TryParse(parts[1], out var virtualId))
        return Results.NotFound();

    var raw = await rxClient.GetDocumentAsync(parts[0], ct);
    var docNode = JsonNode.Parse(raw);
    var filename = ZgwMapper.FindAttachmentFilename(docNode, virtualId);

    if (string.IsNullOrEmpty(filename))
        return Results.NotFound();

    var (stream, contentType, resolvedName) = await rxClient.DownloadDocumentAsync(parts[0], filename, ct);
    return Results.Stream(stream, contentType, resolvedName);
});

app.Run();

static string Require(IConfiguration config, string key) =>
    config[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"'{key}' is required. Set it in .env or as an environment variable.");
