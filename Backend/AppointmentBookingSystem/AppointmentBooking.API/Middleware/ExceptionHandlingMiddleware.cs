using System.Net;
using System.Text.Json;

namespace AppointmentBooking.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = HttpStatusCode.InternalServerError;
        string message = "An unexpected error occurred while processing your request.";

        switch (exception)
        {
            case KeyNotFoundException keyNotFound:
                statusCode = HttpStatusCode.NotFound;
                message = keyNotFound.Message;
                _logger.LogWarning(exception, "Resource not found: {Message}", keyNotFound.Message);
                break;

            case UnauthorizedAccessException unauthorized:
                statusCode = HttpStatusCode.Unauthorized;
                message = unauthorized.Message;
                _logger.LogWarning(exception, "Unauthorized access attempt: {Message}", unauthorized.Message);
                break;

            case InvalidOperationException invalidOp:
                if (invalidOp.Message.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                    invalidOp.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                    invalidOp.Message.Contains("slot", StringComparison.OrdinalIgnoreCase))
                {
                    statusCode = HttpStatusCode.Conflict;
                }
                else if (invalidOp.Message.Contains("deactivated", StringComparison.OrdinalIgnoreCase) ||
                         invalidOp.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase))
                {
                    statusCode = HttpStatusCode.Forbidden;
                }
                else
                {
                    statusCode = HttpStatusCode.BadRequest;
                }
                message = invalidOp.Message;
                _logger.LogWarning(exception, "Invalid operation: {Message}", invalidOp.Message);
                break;

            case ArgumentException argEx:
                statusCode = HttpStatusCode.BadRequest;
                message = argEx.Message;
                _logger.LogWarning(exception, "Invalid argument: {Message}", argEx.Message);
                break;

            default:
                _logger.LogError(exception, "Unhandled Exception on {Path}: {Message}", context.Request.Path, exception.Message);
                break;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            success = false,
            message,
            data = (object?)null
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
