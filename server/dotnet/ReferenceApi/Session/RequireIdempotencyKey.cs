namespace ReferenceApi.Session;

/// <summary>
/// Turns away an unsafe request (anything but GET, HEAD, OPTIONS) that has no
/// Idempotency-Key, with a 400.
/// </summary>
/// <remarks>
/// WHY: PROTOCOL §1 already says every command MUST carry one. Enforcing it makes it a CSRF
/// defence too: a custom header can't be sent cross-origin without a CORS preflight, which a
/// hostile page won't pass. Together with SameSite=Strict on the session cookie, that's the
/// whole defence, so there's no antiforgery token.
///
/// The /dev endpoints are exempt: they're for curl, and they're not commands.
///
/// FOR REAL: a security review. This is the reasoning, not a sign-off.
/// </remarks>
public static class RequireIdempotencyKey
{
    public static void UseRequireIdempotencyKey(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var unsafeMethod = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
                                 HttpMethods.IsOptions(request.Method));

            if (unsafeMethod
                && !request.Path.StartsWithSegments("/dev")
                && string.IsNullOrWhiteSpace(request.Headers["Idempotency-Key"]))
            {
                await Results.Problem(
                        detail: "This request is missing its Idempotency-Key.",
                        statusCode: StatusCodes.Status400BadRequest)
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}
