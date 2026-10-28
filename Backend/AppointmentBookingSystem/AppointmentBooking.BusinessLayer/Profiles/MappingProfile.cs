using AppointmentBooking.BusinessLayer.Features.Auth.DTOs;
using AppointmentBooking.BusinessLayer.Features.Providers.DTOs;
using AppointmentBooking.BusinessLayer.Features.Tenants.DTOs;
using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using AutoMapper;

namespace AppointmentBooking.BusinessLayer.Profiles;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // User Mappings
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => 
                string.IsNullOrWhiteSpace(src.LastName) ? src.FirstName : $"{src.FirstName} {src.LastName}".Trim()))
            .ForMember(dest => dest.TenantName, opt => opt.MapFrom(src => src.Tenant != null ? src.Tenant.Name : null))
            .ForMember(dest => dest.ServiceProviderId, opt => opt.MapFrom(src => src.ServiceProvider != null ? src.ServiceProvider.Id : (int?)null));

        // Tenant Mappings (Optimized - Only requires Users navigation)
        CreateMap<Tenant, TenantDto>()
            .ForMember(dest => dest.TenantCode, opt => opt.MapFrom(src => src.TenantCode))
            .ForMember(dest => dest.TotalProvidersCount, opt => opt.MapFrom(src => src.Users.Count(u => !u.IsDeleted && u.Role == UserRole.ServiceProvider)))
            .ForMember(dest => dest.TotalAppointmentsCount, opt => opt.MapFrom(_ => 0))
            .ForMember(dest => dest.AdminFirstName, opt => opt.MapFrom(src => src.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.FirstName).FirstOrDefault()))
            .ForMember(dest => dest.AdminLastName, opt => opt.MapFrom(src => src.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.LastName).FirstOrDefault()))
            .ForMember(dest => dest.AdminEmail, opt => opt.MapFrom(src => src.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Email).FirstOrDefault()))
            .ForMember(dest => dest.ServiceTypes, opt => opt.MapFrom(_ => new List<string>()))
            .ForMember(dest => dest.Rating, opt => opt.MapFrom(_ => 4.8))
            .ForMember(dest => dest.TotalReviewsCount, opt => opt.MapFrom(_ => 1200))
            .ForMember(dest => dest.Slug, opt => opt.MapFrom(src => 
                !string.IsNullOrWhiteSpace(src.TenantCode) 
                    ? src.TenantCode 
                    : System.Text.RegularExpressions.Regex.Replace(src.Name.ToLower().Trim(), @"[^a-z0-9]+", "-").Trim('-')));

        // ServiceProvider Mappings
        CreateMap<ServiceProvider, ServiceProviderDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.User != null ? $"{src.User.FirstName} {src.User.LastName}".Trim() : string.Empty))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User != null ? src.User.Email : string.Empty))
            .ForMember(dest => dest.PhoneNumber, opt => opt.MapFrom(src => src.User != null ? (src.User.PhoneNumber ?? string.Empty) : string.Empty))
            .ForMember(dest => dest.Address, opt => opt.MapFrom(src => src.User != null ? src.User.Address : null))
            .ForMember(dest => dest.TotalAppointmentsCount, opt => opt.MapFrom(src => 
                src.Appointments != null 
                    ? src.Appointments.Count(a => 
                        a.Status == AppointmentStatus.Booked || 
                        a.Status == AppointmentStatus.Approved || 
                        a.Status == AppointmentStatus.RescheduleProposed ||
                        a.Status == AppointmentStatus.Rescheduled) 
                    : 0));

        CreateMap<CreateServiceProviderRequestDto, ServiceProvider>()
            .ForMember(dest => dest.ServiceType, opt => opt.MapFrom(src => src.ServiceType.Trim()))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(_ => ProviderStatus.Active));

        CreateMap<UpdateServiceProviderRequestDto, ServiceProvider>()
            .ForMember(dest => dest.ServiceType, opt => opt.MapFrom(src => src.ServiceType.Trim()));

        // Tenant Request Mappings
        CreateMap<CreateTenantRequestDto, Tenant>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.TenantName.Trim()))
            .ForMember(dest => dest.ContactEmail, opt => opt.MapFrom(src => src.ContactEmail.Trim().ToLower()))
            .ForMember(dest => dest.ContactPhone, opt => opt.MapFrom(src => !string.IsNullOrWhiteSpace(src.ContactPhone) ? src.ContactPhone.Trim() : null))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(_ => TenantStatus.Active));

        CreateMap<UpdateTenantRequestDto, Tenant>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.TenantName.Trim()))
            .ForMember(dest => dest.ContactEmail, opt => opt.MapFrom(src => src.ContactEmail != null ? src.ContactEmail.Trim().ToLower() : null))
            .ForMember(dest => dest.ContactPhone, opt => opt.MapFrom(src => src.ContactPhone != null ? src.ContactPhone.Trim() : null));

        // Customer Registration Mapping
        CreateMap<RegisterCustomerRequestDto, User>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email.Trim().ToLower()))
            .ForMember(dest => dest.Role, opt => opt.MapFrom(_ => UserRole.Customer))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(_ => true));

        // Provider Availability Mappings
        CreateMap<ProviderAvailability, AppointmentBooking.BusinessLayer.Features.Availability.DTOs.ProviderAvailabilityDto>()
            .ForMember(dest => dest.ServiceProviderName, opt => opt.MapFrom(src => src.ServiceProvider != null ? src.ServiceProvider.Name : string.Empty))
            .ForMember(dest => dest.StartTime, opt => opt.MapFrom(src => src.StartTime.ToString(@"hh\:mm")))
            .ForMember(dest => dest.EndTime, opt => opt.MapFrom(src => src.EndTime.ToString(@"hh\:mm")))
            .ForMember(dest => dest.BreakStartTime, opt => opt.MapFrom(src => src.BreakStartTime.HasValue ? src.BreakStartTime.Value.ToString(@"hh\:mm") : null))
            .ForMember(dest => dest.BreakEndTime, opt => opt.MapFrom(src => src.BreakEndTime.HasValue ? src.BreakEndTime.Value.ToString(@"hh\:mm") : null));

        // Appointment Mappings
        CreateMap<Appointment, AppointmentBooking.BusinessLayer.Features.Appointments.DTOs.AppointmentDto>()
            .ForMember(dest => dest.ServiceProviderName, opt => opt.MapFrom(src => src.ServiceProvider != null ? src.ServiceProvider.Name : string.Empty))
            .ForMember(dest => dest.ServiceProviderType, opt => opt.MapFrom(src => src.ServiceProvider != null ? src.ServiceProvider.ServiceType : string.Empty))

            .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? (string.IsNullOrWhiteSpace(src.Customer.LastName) ? src.Customer.FirstName : $"{src.Customer.FirstName} {src.Customer.LastName}".Trim()) : string.Empty))
            .ForMember(dest => dest.CustomerEmail, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Email : string.Empty))
            .ForMember(dest => dest.CustomerPhone, opt => opt.MapFrom(src => src.Customer != null ? (src.Customer.PhoneNumber ?? string.Empty) : string.Empty))
            .ForMember(dest => dest.CustomerAddress, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Address : null))
            .ForMember(dest => dest.StartTime, opt => opt.MapFrom(src => src.StartTime.ToString(@"hh\:mm")))
            .ForMember(dest => dest.EndTime, opt => opt.MapFrom(src => src.EndTime.ToString(@"hh\:mm")))
            .ForMember(dest => dest.ProposedStartTime, opt => opt.MapFrom(src => src.ProposedStartTime.HasValue ? src.ProposedStartTime.Value.ToString(@"hh\:mm") : null))
            .ForMember(dest => dest.ProposedEndTime, opt => opt.MapFrom(src => src.ProposedEndTime.HasValue ? src.ProposedEndTime.Value.ToString(@"hh\:mm") : null));
    }
}
