using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Appointments;

public interface IAppointmentService
{
    // Customer Lookup & Search for Assisted Booking (Admin & Service Provider)
    Task<UserDto?> LookupCustomerAsync(int? tenantId, string query, CancellationToken cancellationToken = default);
    Task<List<UserDto>> SearchCustomersAsync(int? tenantId, string query, CancellationToken cancellationToken = default);

    // Module 3: Available Slots for Customer & Admin
    Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsAsync(int providerId, DateTime date, CancellationToken cancellationToken = default);

    // Module 4: Book Appointment (Supports Guest, Registered Customer & Optional Password Auto-Account Creation)
    Task<ApiResponse<AppointmentDto>> BookAppointmentAsync(int? tenantId, int? customerId, BookAppointmentRequestDto request, bool isBookedByProvider = false, string? createdByName = null, CancellationToken cancellationToken = default);

    // Module 5: Reschedule & Cancel (Supports Admin, Logged-in Customer & Guest)
    Task<ApiResponse<AppointmentDto>> RescheduleAppointmentAsync(int appointmentId, int? tenantId, int? customerId, bool isAdmin, RescheduleAppointmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AppointmentDto>> CancelAppointmentAsync(int appointmentId, int? tenantId, int? customerId, bool isAdmin, CancelAppointmentRequestDto request, string? cancellerName = null, CancellationToken cancellationToken = default);

    // Customer History Portals
    Task<List<AppointmentDto>> GetMyAppointmentsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<PagedResult<AppointmentDto>> GetMyAppointmentsPagedAsync(int customerId, PaginationParams paginationParams, AppointmentStatus? status = null, CancellationToken cancellationToken = default);
    Task<AppointmentStatsDto> GetCustomerStatsAsync(int customerId, CancellationToken cancellationToken = default);

    // Module 7: Admin & Provider Dashboard, List & Calendar
    Task<List<AppointmentDto>> GetAppointmentsAsync(int tenantId, int? providerId, DateTime? startDate, DateTime? endDate, AppointmentStatus? status, string? search, CancellationToken cancellationToken = default);
    Task<PagedResult<AppointmentDto>> GetAppointmentsPagedAsync(int tenantId, int? providerId, DateTime? startDate, DateTime? endDate, AppointmentStatus? status, PaginationParams paginationParams, CancellationToken cancellationToken = default);
    Task<AppointmentDto?> GetAppointmentByIdAsync(int appointmentId, int tenantId, CancellationToken cancellationToken = default);

    // Unified Status Management (Approve, Reject, Complete, Cancel by Provider / Admin)
    Task<ApiResponse<AppointmentDto>> UpdateStatusAsync(int appointmentId, int? tenantId, int? providerId, int? currentUserId, bool isAdmin, UpdateAppointmentStatusDto request, string? cancellerName = null, CancellationToken cancellationToken = default);
    Task<ApiResponse<AppointmentDto>> RespondToRescheduleProposalAsync(int appointmentId, int customerId, bool accept, CancellationToken cancellationToken = default);
    Task<AppointmentStatsDto> GetStatsAsync(int tenantId, int? providerId = null, CancellationToken cancellationToken = default);
}
