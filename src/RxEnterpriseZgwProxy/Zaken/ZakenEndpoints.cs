using RxEnterprise.Client;
using RxEnterpriseZgwProxy.Shared;

namespace RxEnterpriseZgwProxy.Zaken;

public static class ZakenEndpoints
{
    public static IEndpointRouteBuilder MapZakenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/zaken/api/v1/zaken", async (
            HttpRequest request,
            IRxEnterpriseClient rxClient,
            CancellationToken ct) =>
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";

            var query = request.Query["identificatie"].FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
                return Results.Ok(ZakenMapper.ToPaginatedResult(Array.Empty<ZgwZaak>()));
            var zaken = await rxClient.SearchZaakAsync(query, ct);

            var zaakObjects = zaken.Select(zaak =>
            {
                var selfUrl = $"{baseUrl}/zaken/api/v1/zaken/{zaak.Sleutel}";
                return ZakenMapper.ToZgwZaak(zaak, selfUrl, baseUrl);
            }).ToArray();

            return Results.Ok(ZakenMapper.ToPaginatedResult(zaakObjects));
        });

        // Get single zaak by Rx.Enterprise ID
        app.MapGet("/zaken/api/v1/zaken/{id}", async (
            string id,
            HttpRequest request,
            IRxEnterpriseClient rxClient,
            CancellationToken ct) =>
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var sleutel = ZaakUuid.ToSleutel(id);
            var selfUrl = $"{baseUrl}/zaken/api/v1/zaken/{sleutel}";
            var zaak = await rxClient.GetZaakAsync(sleutel, ct);
            return Results.Ok(ZakenMapper.ToZgwZaak(zaak, selfUrl, baseUrl));
        });

        app.MapGet("/zaken/api/v1/rollen", async (
            HttpRequest request,
            IRxEnterpriseClient rxClient,
            CancellationToken ct) =>
        {
            var zaakUrl = request.Query["zaak"].FirstOrDefault() ?? string.Empty;
            var zaakId = ZaakUuid.ToSleutel(zaakUrl.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty);

            if (string.IsNullOrEmpty(zaakId))
                return Results.Ok(ZakenMapper.ToPaginatedResult(Array.Empty<ZgwRol>()));

            var zaak = await rxClient.GetZaakAsync(zaakId, ct);
            return Results.Ok(ZakenMapper.ToPaginatedResult(ZakenMapper.ToZgwRollen(zaak)));
        });

        app.MapGet("/zaken/api/v1/statussen", () =>
            Results.Ok(ZakenMapper.ToPaginatedResult(Array.Empty<ZgwStatus>())));

        app.MapGet("/zaken/api/v1/statussen/{id}", (string id, HttpRequest request) =>
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var selfUrl = $"{baseUrl}/zaken/api/v1/statussen/{id}";
            return Results.Ok(ZakenMapper.ToZgwStatus(Base64Encoder.Decode(id), selfUrl, baseUrl));
        });

        app.MapGet("/api/zaken/statussen/{id}", (string id, HttpRequest request) =>
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var selfUrl = $"{baseUrl}/api/zaken/statussen/{id}";
            return Results.Ok(ZakenMapper.ToZgwStatus(Base64Encoder.Decode(id), selfUrl, baseUrl));
        });

        app.MapGet("/zaken/api/v1/zaakinformatieobjecten", async (
            HttpRequest request,
            IRxEnterpriseClient rxClient,
            CancellationToken ct) =>
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";
            var zaakUrl = request.Query["zaak"].FirstOrDefault() ?? string.Empty;
            var zaakId = ZaakUuid.ToSleutel(zaakUrl.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty);

            if (string.IsNullOrEmpty(zaakId))
                return Results.Ok(Array.Empty<ZgwZaakInformatieObject>());

            var docs = await rxClient.SearchZaakDocumentsAsync(zaakId, ct);
            return Results.Ok(ZakenMapper.ToZaakInformatieObjecten(docs, zaakUrl, baseUrl));
        });
        
        return app;
    }
}
