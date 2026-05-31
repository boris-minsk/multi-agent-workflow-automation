using System.Buffers.Binary;

namespace MultiAgent.Infrastructure.Crm;

/// <summary>
/// Reversible, stateless mapping between a HubSpot numeric object id (a string holding an
/// Int64) and the <see cref="Guid"/> that <c>ICrmRepository</c> uses as a lead identity.
/// The id is packed into the last 8 bytes of a Guid behind a fixed 8-byte marker, so it can
/// be recovered later without any lookup table — <c>List</c> and <c>Get</c> stay consistent
/// across DI scopes/requests.
/// </summary>
internal static class HubSpotIds
{
    // ASCII "HSCONTCT" — marks a Guid as derived from a HubSpot contact id.
    private static readonly byte[] Marker = "HSCONTCT"u8.ToArray();

    public static Guid ToGuid(long contactId)
    {
        Span<byte> bytes = stackalloc byte[16];
        Marker.CopyTo(bytes);
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], contactId);
        return new Guid(bytes);
    }

    public static bool TryGetContactId(Guid id, out long contactId)
    {
        Span<byte> bytes = stackalloc byte[16];
        // new Guid(span) and TryWriteBytes are exact inverses, so the marker round-trips.
        if (!id.TryWriteBytes(bytes) || !bytes[..8].SequenceEqual(Marker))
        {
            contactId = 0;
            return false;
        }

        contactId = BinaryPrimitives.ReadInt64BigEndian(bytes[8..]);
        return true;
    }
}
