using CashFlow.Transactions.Domain.Exceptions;

namespace CashFlow.Transactions.Domain.Models;

public static class BusinessCalendar
{
    public static DateOnly CurrentDate(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new InvalidUtcTimestampException();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
    }
}
