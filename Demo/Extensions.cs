using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;

namespace Demo;

public static class Extensions
{

    public static bool IsAjax(this HttpRequest request)
    {
        return request.Headers.XRequestedWith == "XMLHttpRequest";
    }

    public static bool IsValid(this ModelStateDictionary ms, string key)
    {
        return ms.GetFieldValidationState(key) == ModelValidationState.Valid;
    }

    public static DateOnly ToDateOnly(this DateTime dt)
    {
        return DateOnly.FromDateTime(dt);
    }

    public static TimeOnly ToTimeOnly(this DateTime dt)
    {
        return TimeOnly.FromDateTime(dt);
    }

    public static DateTime ToMalaysiaTime(this DateTime utc)
    {
        // Malaysia Standard Time is UTC+8 year-round — no DST to worry about.
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(8);
    }



    // ------------------------------------------------------------------------
    // Session Extension Methods
    // ------------------------------------------------------------------------

    public static void Set<T>(this ISession session, string key, T value)
    {
        session.SetString(key, JsonSerializer.Serialize(value));
    }

    public static T? Get<T>(this ISession session, string key)
    {
        var value = session.GetString(key);
        return value == null ? default : JsonSerializer.Deserialize<T>(value);
    }
}
