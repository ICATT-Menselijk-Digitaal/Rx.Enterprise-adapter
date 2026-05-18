using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RxEnterpriseZgwProxy.Zgw;

public static class ZgwMapper
{
    public static JsonObject ToPaginatedResult(JsonObject[] zaken) => new()
    {
        ["count"] = zaken.Length,
        ["next"] = null,
        ["previous"] = null,
        ["results"] = new JsonArray(zaken.Select(z => (JsonNode)z).ToArray()),
    };

    public static JsonObject ToZgwZaak(JsonNode? rxZaak, string selfUrl, string baseUrl)
    {
        var boekdatum = ExtractBoekdatum(rxZaak);

        return new JsonObject
        {
            ["url"] = selfUrl,
            ["uuid"] = ExtractId(rxZaak),
            ["identificatie"] = ExtractSleutel(rxZaak),
            ["omschrijving"] = ExtractBetreft(rxZaak),
            ["bronorganisatie"] = string.Empty,
            ["zaaktype"] = ExtractZaaktypeName(rxZaak),
            ["registratiedatum"] = boekdatum,
            ["startdatum"] = boekdatum,
            ["status"] = string.Empty, //todo ExtractAfhandelingsstatus(rxZaak) - will be tested,
            ["toelichting"] = ExtractToelichting(rxZaak),
        };
    }

    public static JsonObject ToZgwStatus(string statusName, string selfUrl, string baseUrl) => new()
    {
        ["url"] = selfUrl,
        ["uuid"] = EncodeBase64Segment(statusName),
        ["zaak"] = null,
        ["statustype"] = $"{baseUrl}/catalogi/api/v1/statustypen/{EncodeBase64Segment(statusName)}",
        ["datumStatusGezet"] = null,
        ["statustoelichting"] = statusName,
        ["indicatieLaatstGezetteStatus"] = null,
    };

    // Returns the Rx.Enterprise ID used as path segment in the self URL.
    public static string ExtractId(JsonNode? rxZaak) =>
        rxZaak?["sleutel"]?.ToString() ?? "";

    // Unwraps a raw Rx.Enterprise response into individual zaak nodes.
    public static IEnumerable<JsonNode?> Unwrap(JsonNode? rxResponse)
    {
        if (rxResponse is JsonArray arr)
            return arr.AsEnumerable();

        if (rxResponse is JsonObject obj)
        {
            var inner = obj["results"] ?? obj["items"] ?? obj["zaken"];
            if (inner is JsonArray innerArr)
                return innerArr.AsEnumerable();

            return [rxResponse];
        }

        return [];
    }

    public static string DeriveUuid(string input) =>
        string.IsNullOrEmpty(input)
            ? Guid.Empty.ToString()
            : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToString();

    public static string EncodeBase64Segment(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    // Decodes a URL-safe base64 path segment back to the original string.
    public static string DecodeBase64Segment(string segment)
    {
        try
        {
            var padded = segment.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch
        {
            return segment;
        }
    }

    // Returns the document number (e.g. "D2025-01-000009") from a zaak or document node.
    public static string? ExtractDocumentNummer(JsonNode? node) =>
        node?["documentnummer"]?.ToString();

    // Builds the array of ZGW zaakinformatieobjecten from a zaak's bijlageinfo list.
    // Returns the full ZGW shape KISS-frontend expects, one entry per attachment.
    public static JsonArray ToZaakInformatieObjecten(JsonNode? docNode, string zaakUrl, string baseUrl)
    {
        var documentNummer = ExtractDocumentNummer(docNode);
        var bijlageInfo = docNode?["bijlageinfo"] as JsonArray;

        if (string.IsNullOrEmpty(documentNummer) || bijlageInfo == null)
            return [];

        var result = new JsonArray();
        foreach (var item in bijlageInfo)
        {
            var virtualId = item?["virtualid"]?.GetValue<int>() ?? 0;
            if (virtualId == 0) continue;

            var compoundId = $"{documentNummer}--{virtualId}";
            var uuid = DeriveUuid(compoundId);
            var encodedId = Uri.EscapeDataString(compoundId);
            var registratiedatum = ParallelArrayValue(docNode, "bijlagedatumtijd", virtualId - 1) ?? string.Empty;

            result.Add(new JsonObject
            {
                ["url"] = $"{baseUrl}/zaken/api/v1/zaakinformatieobjecten/{uuid}",
                ["uuid"] = uuid,
                ["informatieobject"] = $"{baseUrl}/documenten/api/v1/enkelvoudiginformatieobjecten/{encodedId}",
                ["zaak"] = zaakUrl,
                ["aardRelatieWeergave"] = "",
                ["titel"] = string.Empty,
                ["beschrijving"] = string.Empty,
                ["registratiedatum"] = registratiedatum,
                ["vernietigingsdatum"] = null,
                ["status"] = null,
            });
        }
        return result;
    }

    public static JsonObject ToEnkelvoudigInformatieObject(JsonNode? docNode, int virtualId, string selfUrl)
    {
        var idx = virtualId - 1;
        var bijlageInfo = docNode?["bijlageinfo"] as JsonArray;
        var item = bijlageInfo?.FirstOrDefault(x => x?["virtualid"]?.GetValue<int>() == virtualId);

        var filename = item?["filename"]?.ToString() ?? string.Empty;
        var titel = ParallelArrayValue(docNode, "bijlageomschrijving", idx) ?? filename;
        var contentType = ParallelArrayValue(docNode, "bijlagecontenttype", idx) ?? "application/octet-stream";
        var grootteStr = ParallelArrayValue(docNode, "bijlagegrootte", idx);
        var bestandsomvang = int.TryParse(grootteStr, out var size) ? size : 0;

        return new JsonObject
        {
            ["url"] = selfUrl,
            ["identificatie"] = docNode?["documentnummer"]?.ToString() ?? string.Empty,
            ["bronorganisatie"] = string.Empty,
            ["creatiedatum"] = docNode?["creatiedatum"]?.ToString() ?? string.Empty,
            ["titel"] = titel,
            ["vertrouwelijkheidaanduiding"] = "openbaar",
            ["formaat"] = contentType,
            ["bestandsnaam"] = filename,
            ["bestandsomvang"] = bestandsomvang,
            ["inhoud"] = $"{selfUrl}/download",
        };
    }

    public static string? FindAttachmentFilename(JsonNode? docNode, int virtualId) =>
        (docNode?["bijlageinfo"] as JsonArray)
            ?.FirstOrDefault(x => x?["virtualid"]?.GetValue<int>() == virtualId)
            ?["filename"]?.ToString();

    private static string? ParallelArrayValue(JsonNode? node, string key, int index) =>
        (node?[key] as JsonArray)?.ElementAtOrDefault(index)?.ToString();

    private static string ExtractSleutel(JsonNode? n) =>
        n?["sleutel"]?.GetValue<string>()
        ?? string.Empty;

    private static string ExtractBetreft(JsonNode? n) =>
        n?["betreft"]?.GetValue<string>()
        ?? string.Empty;

    private static string ExtractBoekdatum(JsonNode? n) =>
        n?["boekdatum"]?.GetValue<string>()
        ?? string.Empty;

    private static string ExtractZaaktypeName(JsonNode? n) =>
        n?["betreft"]?.GetValue<string>()
        ?? string.Empty;

    private static string ExtractAfhandelingsstatus(JsonNode? n) =>
        n?["afhandelingsstatus"]?.GetValue<string>()
        ?? string.Empty;

    private static string ExtractToelichting(JsonNode? n) =>
        n?["toelichting"]?.GetValue<string>()
        ?? n?["notitie"]?.GetValue<string>()
        ?? n?["omschrijving"]?.GetValue<string>()
        ?? string.Empty;
}
