using EventRegistration.Api.DTOs;

namespace EventRegistration.Api.Services;

/// <summary>Converts <see cref="BookingException"/> into the uniform JSON error body.</summary>
public class BookingExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BookingException exception) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = exception.StatusCode;
            await context.Response.WriteAsJsonAsync(new ApiErrorBody(new ApiError(exception.Code, exception.Message)));
        }
    }
}
