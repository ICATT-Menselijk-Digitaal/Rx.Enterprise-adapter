using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RxEnterprise.Client;

internal sealed partial class RxEnterpriseClient(HttpClient httpClient) : IRxEnterpriseClient
{
    [GeneratedRegex(@"new Date\((\d+)\)")]
    private static partial Regex NewDateRegex();

    public async Task<IEnumerable<RxZaak>> SearchZaakAsync(string query, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/zaak?search={Uri.EscapeDataString(query)}", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        // Rx.Enterprise answers a search without hits with 200 and an empty body.
        if (string.IsNullOrWhiteSpace(json)) return [];
        return UnwrapZaken(JsonNode.Parse(json));
    }

    public async Task<RxZaak> GetZaakAsync(string zaakId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/zaak/{zaakId}", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        return JsonSerializer.Deserialize<RxZaak>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize zaak '{zaakId}'");
    }

    public async Task<RxDocument> GetDocumentAsync(string documentId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/document/{Uri.EscapeDataString(documentId)}", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        return JsonSerializer.Deserialize<RxDocument>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize document '{documentId}'");
    }

    public async Task<RxZaaktype> GetZaaktypeAsync(string sleutel, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/zaaktype/{Uri.EscapeDataString(sleutel)}", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        return JsonSerializer.Deserialize<RxZaaktype>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize zaaktype '{sleutel}'");
    }

    public async Task<(Stream Content, string ContentType, string FileName)> DownloadDocumentAsync(
        string documentId, string fileName, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"data/document/{Uri.EscapeDataString(documentId)}/{Uri.EscapeDataString(fileName)}", ct);
        await EnsureSuccess(response, ct);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var resolvedName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? fileName;

        return (await response.Content.ReadAsStreamAsync(ct), contentType, resolvedName);
    }

    public async Task<IEnumerable<RxZaakDocument>> SearchZaakDocumentsAsync(string zaaknummer, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"api/zaak-document/search?search=[bronsleutel]=\"{zaaknummer}\"", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<RxZaakDocument>>(json) ?? [];
    }

    public async Task<RxZaakDocument?> GetZaakDocumentAsync(string doelsleutel, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"api/zaak-document/search?search=[doelsleutel]=\"{doelsleutel}\"", ct);
        await EnsureSuccess(response, ct);
        var json = Sanitize(await response.Content.ReadAsStringAsync(ct));
        if (string.IsNullOrWhiteSpace(json)) return null;
        var results = JsonSerializer.Deserialize<List<RxZaakDocument>>(json) ?? [];
        return results.FirstOrDefault();
    }

    public async Task<(Stream Content, string ContentType, string FileName)> DownloadFromRelativeUrlAsync(
        string relativeUrl, CancellationToken ct = default)
    {
        var url = relativeUrl.TrimStart('/');
        var response = await httpClient.GetAsync(url, ct);
        await EnsureSuccess(response, ct);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? Path.GetFileName(relativeUrl);

        return (await response.Content.ReadAsStreamAsync(ct), contentType, fileName);
    }

    private static IEnumerable<RxZaak> UnwrapZaken(JsonNode? node)
    {
        if (node is JsonArray arr)
            return arr.Select(n => n.Deserialize<RxZaak>()!);

        if (node is JsonObject obj)
        {
            var inner = obj["results"] ?? obj["items"] ?? obj["zaken"];
            if (inner is JsonArray innerArr)
                return innerArr.Select(n => n.Deserialize<RxZaak>()!);

            return [obj.Deserialize<RxZaak>()!];
        }

        return [];
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var body = System.Text.Encoding.UTF8.GetString(bytes);
        throw new HttpRequestException(
            $"Rx.Enterprise returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
            inner: null,
            response.StatusCode);
    }

    // done to workaround Rx.Enterprise's weird date format which is not directly parseable by System.Text.Json.
    // i.e. instead of "2024-01-01" we get new Date(1704067200000)
    private static string Sanitize(string raw) =>
        NewDateRegex().Replace(raw, m =>
        {
            var ms = long.Parse(m.Groups[1].Value);
            return $"\"{DateTimeOffset.FromUnixTimeMilliseconds(ms):yyyy-MM-dd}\"";
        });
}
