namespace AppointmentBooking.BusinessLayer.Common.Models;

/// <summary>
/// Uniform standardized API response wrapper: { success, message, data }
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T? data, string message = "Success") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Message = message, Data = default };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string message = "Success") =>
        new() { Success = true, Message = message, Data = null };

    public static new ApiResponse Fail(string message) =>
        new() { Success = false, Message = message, Data = null };
}
