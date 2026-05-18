using System.Text.RegularExpressions;

namespace RxEnterprise.Client;

internal sealed partial class RxEnterpriseClient(HttpClient httpClient) : IRxEnterpriseClient
{
    [GeneratedRegex(@"new Date\((\d+)\)")]
    private static partial Regex NewDateRegex();

    public async Task<string> GetZaakAsync(string zaakId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/zaak/{zaakId}", ct);
        response.EnsureSuccessStatusCode();
        return Sanitize(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<string> SearchZaakAsync(string query, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/zaak?search={Uri.EscapeDataString(query)}", ct);
        response.EnsureSuccessStatusCode();
        return Sanitize(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<string> GetDocumentAsync(string documentId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"data/document/{Uri.EscapeDataString(documentId)}", ct);
        response.EnsureSuccessStatusCode();
        return Sanitize(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<(Stream Content, string ContentType, string FileName)> DownloadDocumentAsync(
        string documentId, string fileName, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync(
            $"data/document/{Uri.EscapeDataString(documentId)}/{Uri.EscapeDataString(fileName)}", ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var resolvedName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? fileName;

        return (await response.Content.ReadAsStreamAsync(ct), contentType, resolvedName);
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
