namespace EventRegistration.Api.Services;

/// <summary>
/// An expected business failure that maps to a structured HTTP error.
/// Thrown by services and translated by <see cref="BookingExceptionMiddleware"/>.
/// </summary>
public class BookingException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;

    public static BookingException Validation(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
    public static BookingException NotFound(string message) => new(StatusCodes.Status404NotFound, "not_found", message);
    public static BookingException Conflict(string code, string message) => new(StatusCodes.Status409Conflict, code, message);
    public static BookingException ProviderUnavailable(string message) => new(StatusCodes.Status503ServiceUnavailable, "payment_provider_unavailable", message);
}
