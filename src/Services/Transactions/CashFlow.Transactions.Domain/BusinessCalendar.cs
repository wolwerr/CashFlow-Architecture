namespace CashFlow.Transactions.Domain;

public static class BusinessCalendar
{
    public static DateOnly CurrentDate(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A UTC instant is required.", nameof(nowUtc));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
    }
}
