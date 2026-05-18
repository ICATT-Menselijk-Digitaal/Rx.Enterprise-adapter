namespace RxEnterprise.Client;

public interface IRxEnterpriseClient
{
    Task<string> GetZaakAsync(string zaakId, CancellationToken ct = default);
    Task<string> SearchZaakAsync(string query, CancellationToken ct = default);
    Task<string> GetDocumentAsync(string documentId, CancellationToken ct = default);
    Task<(Stream Content, string ContentType, string FileName)> DownloadDocumentAsync(string documentId, string fileName, CancellationToken ct = default);
}
