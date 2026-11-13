using System.Security.Cryptography;
using System.Text.Json;

namespace GrowLog.Api.Data;

/// <summary>A stable digest of a bound request, before generated identifiers and clock adjustments.</summary>
internal static class RequestFingerprint
{
    public static string For<T>(T request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    public static ValueTask<object?> Capture(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.FirstOrDefault(argument => argument?.GetType().Name.EndsWith("Request", StringComparison.Ordinal) == true);
        context.HttpContext.RequestServices.GetRequiredService<DeviceClock>().RequestFingerprint = For(new { Path = context.HttpContext.Request.Path.Value, Request = request });
        return next(context);
    }
}
