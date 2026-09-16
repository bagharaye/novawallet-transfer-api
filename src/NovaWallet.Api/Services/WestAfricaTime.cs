namespace NovaWallet.Api.Services;

/// <summary>
/// Nigeria/West Africa Time is a fixed UTC+1 offset with no daylight-saving
/// adjustment, so it's computed directly rather than via the host's IANA
/// timezone database (which may not even carry "Africa/Lagos" in a minimal
/// container image, and must never be confused with the server's own local
/// timezone regardless of where it's deployed).
/// </summary>
public static class WestAfricaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    public static DateOnly CalendarDate(DateTimeOffset utcInstant)
    {
        var watInstant = utcInstant.ToOffset(Offset);
        return DateOnly.FromDateTime(watInstant.DateTime);
    }
}
