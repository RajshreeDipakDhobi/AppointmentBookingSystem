using AppointmentBooking.BusinessLayer.Common.Models;
using AppointmentBooking.BusinessLayer.Features.Appointments.DTOs;
using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using AppointmentBooking.DomainLayer.Enums;

namespace AppointmentBooking.BusinessLayer.Features.Appointments;

public interface IAppointmentService
{
    // Customer Lookup & Search for Assisted Booking (Admin & Service Provider)
    Task<UserDto?> LookupCustomerAsync(int? tenantId, string query);
    Task<List<UserDto>> SearchCustomersAsync(int? tenantId, string query);

    // Module 3: Available Slots for Customer & Admin
    Task<ProviderDailyAvailabilityResponseDto?> GetAvailableSlotsAsync(int providerId, DateTime date);

    // Module 4: Book Appointment (Supports Guest, Registered Customer & Optional Password Auto-Account Creation)
    Task<ApiResponse<AppointmentDto>> BookAppointmentAsync(int? tenantId, int? customerId, BookAppointmentRequestDto request);

    // Module 5: Reschedule & Cancel (Supports Admin, Logged-in Customer & Guest)
    Task<ApiResponse<AppointmentDto>> RescheduleAppointmentAsync(int appointmentId, int? tenantId, int? customerId, bool isAdmin, RescheduleAppointmentRequestDto request);
    Task<ApiResponse<AppointmentDto>> CancelAppointmentAsync(int appointmentId, int? tenantId, int? customerId, bool isAdmin, CancelAppointmentRequestDto request, string? cancellerName = null);

    // Customer History Portals
    Task<List<AppointmentDto>> GetMyAppointmentsAsync(int customerId);
    Task<PagedResult<AppointmentDto>> GetMyAppointmentsPagedAsync(int customerId, PaginationParams paginationParams, AppointmentStatus? status = null);
    Task<AppointmentStatsDto> GetCustomerStatsAsync(int customerId);

    // Module 7: Admin & Provider Dashboard, List & Calendar
    Task<List<AppointmentDto>> GetAppointmentsAsync(int tenantId, int? providerId, DateTime? startDate, DateTime? endDate, AppointmentStatus? status, string? search);
    Task<PagedResult<AppointmentDto>> GetAppointmentsPagedAsync(int tenantId, int? providerId, DateTime? startDate, DateTime? endDate, AppointmentStatus? status, PaginationParams paginationParams);
    Task<AppointmentDto?> GetAppointmentByIdAsync(int appointmentId, int tenantId);

    // Unified Status Management (Approve, Reject, Complete, Cancel by Provider / Admin)
    Task<ApiResponse<AppointmentDto>> UpdateStatusAsync(int appointmentId, int? tenantId, int? providerId, int? currentUserId, bool isAdmin, UpdateAppointmentStatusDto request, string? cancellerName = null);
    Task<ApiResponse<AppointmentDto>> RespondToRescheduleProposalAsync(int appointmentId, int customerId, bool accept);
    Task<AppointmentStatsDto> GetStatsAsync(int tenantId, int? providerId = null);
}
