using System.Text;

namespace RxEnterpriseZgwProxy.Zaken;

/// <summary>
/// Maps a Rx.Enterprise zaak sleutel (e.g. "2026-001596") to a ZGW uuid and back.
/// ZGW clients such as ITA parse the zaak uuid as a GUID, and store it to fetch the
/// zaak again later, so the mapping has to be reversible (a hash, as used for
/// documents, is not). The sleutel is packed into the GUID bytes:
/// [marker][length][UTF-8 sleutel][zero padding].
/// </summary>
public static class ZaakUuid
{
    private const byte Marker = 0x52; // 'R'
    private const int MaxSleutelBytes = 14;

    /// <summary>
    /// Returns a GUID for the sleutel, or the sleutel itself when it does not fit.
    /// </summary>
    public static string FromSleutel(string? sleutel)
    {
        if (string.IsNullOrEmpty(sleutel))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(sleutel);
        if (bytes.Length > MaxSleutelBytes)
            return sleutel;

        var buffer = new byte[16];
        buffer[0] = Marker;
        buffer[1] = (byte)bytes.Length;
        bytes.CopyTo(buffer, 2);
        return new Guid(buffer).ToString();
    }

    /// <summary>
    /// Returns the sleutel packed in a GUID from <see cref="FromSleutel"/>. Any other
    /// value (such as a plain sleutel stored before this mapping existed) is returned as is.
    /// </summary>
    public static string ToSleutel(string id)
    {
        if (!Guid.TryParse(id, out var guid))
            return id;

        var buffer = guid.ToByteArray();
        var length = buffer[1];
        if (buffer[0] != Marker || length is 0 or > MaxSleutelBytes)
            return id;

        for (var i = 2 + length; i < buffer.Length; i++)
        {
            if (buffer[i] != 0)
                return id;
        }

        return Encoding.UTF8.GetString(buffer, 2, length);
    }
}
