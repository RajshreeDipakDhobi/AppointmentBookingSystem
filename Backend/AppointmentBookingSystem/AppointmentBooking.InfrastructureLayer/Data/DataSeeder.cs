using AppointmentBooking.DomainLayer.Entities;
using AppointmentBooking.DomainLayer.Enums;
using Microsoft.EntityFrameworkCore;

namespace AppointmentBooking.InfrastructureLayer.Data;

public static class DataSeeder
{
    public const string SuperAdminEmail = "superadmin@platform.com";
    public const string SuperAdminPassword = "123456";
    public const string DefaultProviderPassword = "123456";
    public const string DefaultCustomerPassword = "123456";

    /// <summary>
    /// Master seed orchestrator called during application startup.
    /// </summary>
    public static async Task SeedAsync(BookingDbContext context)
    {
        await SeedSuperAdminAsync(context);
        //await SeedTenant1ProvidersAsync(context);
        //await SeedTenant1CustomersAsync(context);
        //await SeedCalendarAppointmentsAsync(context);
    }

    /// <summary>
    /// Seeds the default SuperAdmin user if not already present.
    /// </summary>
    private static async Task SeedSuperAdminAsync(BookingDbContext context)
    {
        var exists = await context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email.ToLower() == SuperAdminEmail.ToLower() || u.Role == UserRole.SuperAdmin);

        if (!exists)
        {
            var superAdmin = new User
            {
                TenantId = null,
                FirstName = "Super",
                LastName = "Admin",
                Email = SuperAdminEmail,
                PhoneNumber = "+1234567890",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(SuperAdminPassword),
                Role = UserRole.SuperAdmin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };

            await context.Users.AddAsync(superAdmin);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds verified service providers for Tenant 1 with default password '123456'.
    /// Includes Ananya Patel and Rajat Dalal with their active weekly availability.
    /// </summary>
    public static async Task SeedTenant1ProvidersAsync(BookingDbContext context)
    {
        const int tenantId = 1;

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
        {
            return;
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(DefaultProviderPassword);

        var seedProviders = new[]
        {
            new
            {
                FirstName = "Rajat",
                LastName = "Dalal",
                Email = "rajat@gmail.com",
                PhoneNumber = "9825123400",
                ServiceType = "Nail Artist",
                Bio = "Creative nail artist specializing in 3D nail art, gel extensions, luxury manicures, and bespoke bridal nail styling.",
                ChargesAmount = 600.00m,
                Address = "Station 3 - Nail & Care Lounge",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=300&h=300&fit=crop&crop=faces"
            },
            new
            {
                FirstName = "Pooja",
                LastName = "Sharma",
                Email = "pooja.sharma@apexsalon.com",
                PhoneNumber = "9825123401",
                ServiceType = "Senior Hair Stylist",
                Bio = "Certified master hair stylist specializing in balayage, creative coloring, and precision hair contouring with 8+ years experience.",
                ChargesAmount = 500.00m,
                Address = "Station 1 - Hair & Styling Bar",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1595959183082-7b570b7e08e2?w=300&h=300&fit=crop&crop=faces"
            },
            new
            {
                FirstName = "Rahul",
                LastName = "Verma",
                Email = "rahul.verma@apexsalon.com",
                PhoneNumber = "9825123402",
                ServiceType = "Master Barber & Grooming",
                Bio = "Men's styling expert with expertise in classic fades, royal shaves, and beard architecture.",
                ChargesAmount = 350.00m,
                Address = "Station 2 - Men's Grooming Wing",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=300&h=300&fit=crop&crop=faces"
            },
            new
            {
                FirstName = "Ananya",
                LastName = "Patel",
                Email = "ananya.patel@apexsalon.com",
                PhoneNumber = "9825123403",
                ServiceType = "Skin & Facial Aesthetician",
                Bio = "Clinical aesthetician and skin health consultant offering hydra-facials, chemical peels, and glowing skincare treatments.",
                ChargesAmount = 800.00m,
                Address = "Room 101 - Skin Care Suite",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=300&h=300&fit=crop&crop=faces"
            },
            new
            {
                FirstName = "Vikram",
                LastName = "Mehta",
                Email = "vikram.mehta@apexsalon.com",
                PhoneNumber = "9825123404",
                ServiceType = "Hair Spa & Scalp Therapy",
                Bio = "Certified trichologist specializing in keratin restoration, ozone scalp treatment, and deep revitalization.",
                ChargesAmount = 1200.00m,
                Address = "Room 102 - Hair Spa Suite",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=300&h=300&fit=crop&crop=faces"
            },
            new
            {
                FirstName = "Sneha",
                LastName = "Joshi",
                Email = "sneha.joshi@apexsalon.com",
                PhoneNumber = "9825123405",
                ServiceType = "Bridal & Makeup Artist",
                Bio = "Celebrity bridal makeup artist specializing in HD airbrush makeup, reception looks, and pre-wedding styling packages.",
                ChargesAmount = 1500.00m,
                Address = "Studio A - Bridal Suite",
                ProfilePictureUrl = "https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=300&h=300&fit=crop&crop=faces"
            }
        };

        foreach (var item in seedProviders)
        {
            var existingUser = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == item.Email.ToLower());

            User user;
            if (existingUser == null)
            {
                user = new User
                {
                    TenantId = tenantId,
                    FirstName = item.FirstName,
                    LastName = item.LastName,
                    Email = item.Email.ToLower(),
                    PhoneNumber = item.PhoneNumber,
                    PasswordHash = passwordHash,
                    Role = UserRole.ServiceProvider,
                    IsActive = true,
                    Address = item.Address,
                    ProfilePictureUrl = item.ProfilePictureUrl,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System"
                };

                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();
            }
            else
            {
                user = existingUser;
                user.PasswordHash = passwordHash;
                user.Role = UserRole.ServiceProvider;
                user.IsActive = true;
                await context.SaveChangesAsync();
            }

            var existingProvider = await context.ServiceProviders
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(sp => sp.UserId == user.Id || (sp.TenantId == tenantId && sp.User.Email.ToLower() == item.Email.ToLower()));

            if (existingProvider == null)
            {
                var provider = new ServiceProvider
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    ServiceType = item.ServiceType,
                    Bio = item.Bio,
                    ChargesAmount = item.ChargesAmount,
                    Status = ProviderStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System"
                };

                await context.ServiceProviders.AddAsync(provider);
                await context.SaveChangesAsync();
                existingProvider = provider;
            }
            else
            {
                existingProvider.ServiceType = item.ServiceType;
                existingProvider.ChargesAmount = item.ChargesAmount;
                existingProvider.Bio = item.Bio;
                existingProvider.Status = ProviderStatus.Active;
                await context.SaveChangesAsync();
            }

            // Ensure weekly operating hours (Monday to Saturday, 10:00 AM - 05:00 PM) for Ananya Patel and Rajat Dalal
            if (item.FirstName == "Ananya" || item.FirstName == "Rajat")
            {
                var activeDays = new[]
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday
                };

                foreach (var day in activeDays)
                {
                    var hasAvail = await context.ProviderAvailabilities
                        .IgnoreQueryFilters()
                        .AnyAsync(pa => pa.ServiceProviderId == existingProvider.Id && pa.DayOfWeek == day && pa.SpecificDate == null);

                    if (!hasAvail)
                    {
                        var avail = new ProviderAvailability
                        {
                            TenantId = tenantId,
                            ServiceProviderId = existingProvider.Id,
                            DayOfWeek = day,
                            SpecificDate = null,
                            StartTime = new TimeSpan(10, 0, 0),
                            EndTime = new TimeSpan(17, 0, 0),
                            BreakStartTime = new TimeSpan(13, 0, 0),
                            BreakEndTime = new TimeSpan(14, 0, 0),
                            SlotDurationMinutes = 30,
                            IsAvailable = true,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "System"
                        };
                        await context.ProviderAvailabilities.AddAsync(avail);
                    }
                }
                await context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Seeds realistic customers in Tenant 1 with Customer role.
    /// </summary>
    public static async Task SeedTenant1CustomersAsync(BookingDbContext context)
    {
        const int tenantId = 1;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(DefaultCustomerPassword);

        var seedCustomers = new[]
        {
            new { FirstName = "Amit", LastName = "Sharma", Email = "amit.sharma@gmail.com", Phone = "9820011001", City = "Mumbai", Address = "Flat 402, Green Glen" },
            new { FirstName = "Neha", LastName = "Verma", Email = "neha.verma@gmail.com", Phone = "9820011002", City = "Mumbai", Address = "B-12, Palm Grove" },
            new { FirstName = "Rohit", LastName = "Mehta", Email = "rohit.mehta@gmail.com", Phone = "9820011003", City = "Mumbai", Address = "701, Oberoi Heights" },
            new { FirstName = "Priya", LastName = "Singh", Email = "priya.singh@gmail.com", Phone = "9820011004", City = "Mumbai", Address = "Plot 15, Hiranandani Gardens" },
            new { FirstName = "Vikram", LastName = "Saini", Email = "vikram.saini@gmail.com", Phone = "9820011005", City = "Mumbai", Address = "Flat 204, Sea Breeze" },
            new { FirstName = "Pooja", LastName = "Iyer", Email = "pooja.iyer@gmail.com", Phone = "9820011006", City = "Mumbai", Address = "D-33, Royal Palms" },
            new { FirstName = "Arjun", LastName = "Desai", Email = "arjun.desai@gmail.com", Phone = "9820011007", City = "Mumbai", Address = "12, Crescent Bay" },
            new { FirstName = "Sneha", LastName = "Kapoor", Email = "sneha.kapoor@gmail.com", Phone = "9820011008", City = "Mumbai", Address = "802, Lodha Park" },
            new { FirstName = "Aarav", LastName = "Malhotra", Email = "aarav.malhotra@gmail.com", Phone = "9820011009", City = "Mumbai", Address = "Villa 5, Sterling Court" },
            new { FirstName = "Simran", LastName = "Kaur", Email = "simran.kaur@gmail.com", Phone = "9820011010", City = "Mumbai", Address = "Flat 101, Silver Sands" },
            new { FirstName = "Kavita", LastName = "Rani", Email = "kavita.rani@gmail.com", Phone = "9820011011", City = "Mumbai", Address = "Block C, Sun Crest" },
            new { FirstName = "Meera", LastName = "Nair", Email = "meera.nair@gmail.com", Phone = "9820011012", City = "Mumbai", Address = "Flat 502, Oceanic View" }
        };

        foreach (var cust in seedCustomers)
        {
            var exists = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Email.ToLower() == cust.Email.ToLower());

            if (!exists)
            {
                var user = new User
                {
                    TenantId = tenantId,
                    FirstName = cust.FirstName,
                    LastName = cust.LastName,
                    Email = cust.Email.ToLower(),
                    PhoneNumber = cust.Phone,
                    PasswordHash = passwordHash,
                    Role = UserRole.Customer,
                    IsActive = true,
                    Address = cust.Address,
                    City = cust.City,
                    State = "Maharashtra",
                    PostalCode = "400001",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System"
                };
                await context.Users.AddAsync(user);
            }
        }
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds rich, realistic appointments across Day, Week, and Month for Ananya Patel and Rajat Dalal.
    /// Fully idempotent: skips appointments that already exist.
    /// </summary>
    public static async Task SeedCalendarAppointmentsAsync(BookingDbContext context)
    {
        const int tenantId = 1;

        // Resolve providers: Ananya Patel & Rajat Dalal
        var ananya = await context.ServiceProviders
            .IgnoreQueryFilters()
            .Include(sp => sp.User)
            .FirstOrDefaultAsync(sp => sp.User.FirstName.ToLower() == "ananya" && sp.User.LastName.ToLower() == "patel");

        var rajat = await context.ServiceProviders
            .IgnoreQueryFilters()
            .Include(sp => sp.User)
            .FirstOrDefaultAsync(sp => sp.User.FirstName.ToLower() == "rajat" && sp.User.LastName.ToLower() == "dalal");

        if (ananya == null || rajat == null)
        {
            return;
        }

        // Cache customer lookup map
        var customers = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Role == UserRole.Customer)
            .ToListAsync();

        int GetCustomerId(string email)
        {
            var user = customers.FirstOrDefault(c => c.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            return user?.Id ?? customers.FirstOrDefault()?.Id ?? 9;
        }

        var appointmentsToSeed = new[]
        {
            // =========================================================================
            // DAY VIEW: TODAY (TUESDAY, OCT 27, 2026) - Strictly 30-min Bookable Slots
            // =========================================================================
            // Ananya Patel (Skin & Facial Aesthetician)
            new { AptNum = "APT-20261027-1001", ProviderId = ananya.Id, CustEmail = "amit.sharma@gmail.com", Reason = "Hydra-Facial Glow Treatment", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Approved, Creator = "Amit Sharma" },
            new { AptNum = "APT-20261027-1002", ProviderId = ananya.Id, CustEmail = "neha.verma@gmail.com", Reason = "Skin Brightening & Peel", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(11, 30, 0), End = new TimeSpan(12, 0, 0), Status = AppointmentStatus.Booked, Creator = "Neha Verma" },
            new { AptNum = "APT-20261027-1003", ProviderId = ananya.Id, CustEmail = "rohit.mehta@gmail.com", Reason = "Acne Clarifying Facial", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Approved, Creator = "Rohit Mehta" },
            new { AptNum = "APT-20261027-1004", ProviderId = ananya.Id, CustEmail = "priya.singh@gmail.com", Reason = "Anti-Aging Collagen Boost", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(15, 30, 0), End = new TimeSpan(16, 0, 0), Status = AppointmentStatus.Booked, Creator = "Priya Singh" },

            // Rajat Dalal (Nail Artist)
            new { AptNum = "APT-20261027-2001", ProviderId = rajat.Id, CustEmail = "vikram.saini@gmail.com", Reason = "Deluxe Gel Manicure", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Approved, Creator = "Vikram Saini" },
            new { AptNum = "APT-20261027-2002", ProviderId = rajat.Id, CustEmail = "pooja.iyer@gmail.com", Reason = "Acrylic Nail Extensions", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(12, 0, 0), End = new TimeSpan(12, 30, 0), Status = AppointmentStatus.Booked, Creator = "Pooja Iyer" },
            new { AptNum = "APT-20261027-2003", ProviderId = rajat.Id, CustEmail = "arjun.desai@gmail.com", Reason = "Nail Art & Cuticle Care", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(14, 30, 0), End = new TimeSpan(15, 0, 0), Status = AppointmentStatus.Approved, Creator = "Arjun Desai" },
            new { AptNum = "APT-20261027-2004", ProviderId = rajat.Id, CustEmail = "sneha.kapoor@gmail.com", Reason = "Express Polish & Buff", Date = new DateTime(2026, 10, 27), Start = new TimeSpan(16, 0, 0), End = new TimeSpan(16, 30, 0), Status = AppointmentStatus.Booked, Creator = "Sneha Kapoor" },

            // =========================================================================
            // DAY / WEEK VIEW: YESTERDAY (MONDAY, OCT 26, 2026)
            // =========================================================================
            new { AptNum = "APT-20261026-1001", ProviderId = ananya.Id, CustEmail = "simran.kaur@gmail.com", Reason = "Deep Pore Cleansing Facial", Date = new DateTime(2026, 10, 26), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Completed, Creator = "Simran Kaur" },
            new { AptNum = "APT-20261026-1002", ProviderId = ananya.Id, CustEmail = "kavita.rani@gmail.com", Reason = "Detox Facial Therapy", Date = new DateTime(2026, 10, 26), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Completed, Creator = "Kavita Rani" },
            new { AptNum = "APT-20261026-2001", ProviderId = rajat.Id, CustEmail = "aarav.malhotra@gmail.com", Reason = "Hand Spa & Nail Shaping", Date = new DateTime(2026, 10, 26), Start = new TimeSpan(11, 0, 0), End = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Completed, Creator = "Aarav Malhotra" },
            new { AptNum = "APT-20261026-2002", ProviderId = rajat.Id, CustEmail = "meera.nair@gmail.com", Reason = "French Manicure Set", Date = new DateTime(2026, 10, 26), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Completed, Creator = "Meera Nair" },

            // =========================================================================
            // WEEK VIEW: WEDNESDAY, OCT 28, 2026
            // =========================================================================
            new { AptNum = "APT-20261028-1001", ProviderId = ananya.Id, CustEmail = "aarav.malhotra@gmail.com", Reason = "Hydra-Facial Express", Date = new DateTime(2026, 10, 28), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Approved, Creator = "Aarav Malhotra" },
            new { AptNum = "APT-20261028-1002", ProviderId = ananya.Id, CustEmail = "simran.kaur@gmail.com", Reason = "Skin Health Consultation", Date = new DateTime(2026, 10, 28), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Booked, Creator = "Simran Kaur" },
            new { AptNum = "APT-20261028-1003", ProviderId = ananya.Id, CustEmail = "sneha.kapoor@gmail.com", Reason = "Vitamin C Infusion Facial", Date = new DateTime(2026, 10, 28), Start = new TimeSpan(15, 30, 0), End = new TimeSpan(16, 0, 0), Status = AppointmentStatus.Approved, Creator = "Sneha Kapoor" },
            new { AptNum = "APT-20261028-2001", ProviderId = rajat.Id, CustEmail = "priya.singh@gmail.com", Reason = "Custom Nail Art & Gel Polish", Date = new DateTime(2026, 10, 28), Start = new TimeSpan(11, 0, 0), End = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Approved, Creator = "Priya Singh" },
            new { AptNum = "APT-20261028-2002", ProviderId = rajat.Id, CustEmail = "neha.verma@gmail.com", Reason = "Nail Strengthening Therapy", Date = new DateTime(2026, 10, 28), Start = new TimeSpan(14, 30, 0), End = new TimeSpan(15, 0, 0), Status = AppointmentStatus.Booked, Creator = "Neha Verma" },

            // =========================================================================
            // WEEK VIEW: THURSDAY, OCT 29, 2026
            // =========================================================================
            new { AptNum = "APT-20261029-1001", ProviderId = ananya.Id, CustEmail = "pooja.iyer@gmail.com", Reason = "Organic Glow Facial", Date = new DateTime(2026, 10, 29), Start = new TimeSpan(11, 0, 0), End = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Approved, Creator = "Pooja Iyer" },
            new { AptNum = "APT-20261029-1002", ProviderId = ananya.Id, CustEmail = "vikram.saini@gmail.com", Reason = "Skin Rejuvenation Session", Date = new DateTime(2026, 10, 29), Start = new TimeSpan(14, 30, 0), End = new TimeSpan(15, 0, 0), Status = AppointmentStatus.Booked, Creator = "Vikram Saini" },
            new { AptNum = "APT-20261029-2001", ProviderId = rajat.Id, CustEmail = "amit.sharma@gmail.com", Reason = "Classic Hand Grooming", Date = new DateTime(2026, 10, 29), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Approved, Creator = "Amit Sharma" },
            new { AptNum = "APT-20261029-2002", ProviderId = rajat.Id, CustEmail = "rohit.mehta@gmail.com", Reason = "Matte Finish Nail Care", Date = new DateTime(2026, 10, 29), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Booked, Creator = "Rohit Mehta" },

            // =========================================================================
            // WEEK VIEW: FRIDAY, OCT 30, 2026
            // =========================================================================
            new { AptNum = "APT-20261030-1001", ProviderId = ananya.Id, CustEmail = "meera.nair@gmail.com", Reason = "Microdermabrasion Treatment", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Approved, Creator = "Meera Nair" },
            new { AptNum = "APT-20261030-1002", ProviderId = ananya.Id, CustEmail = "kavita.rani@gmail.com", Reason = "Hydrating Sheet Mask & Serum", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(12, 0, 0), End = new TimeSpan(12, 30, 0), Status = AppointmentStatus.Booked, Creator = "Kavita Rani" },
            new { AptNum = "APT-20261030-1003", ProviderId = ananya.Id, CustEmail = "simran.kaur@gmail.com", Reason = "Pre-Weekend Radiance Facial", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Approved, Creator = "Simran Kaur" },
            new { AptNum = "APT-20261030-2001", ProviderId = rajat.Id, CustEmail = "arjun.desai@gmail.com", Reason = "Chrome Nail Art Session", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(11, 30, 0), End = new TimeSpan(12, 0, 0), Status = AppointmentStatus.Approved, Creator = "Arjun Desai" },
            new { AptNum = "APT-20261030-2002", ProviderId = rajat.Id, CustEmail = "sneha.kapoor@gmail.com", Reason = "Gel Extension Refill", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Booked, Creator = "Sneha Kapoor" },
            new { AptNum = "APT-20261030-2003", ProviderId = rajat.Id, CustEmail = "aarav.malhotra@gmail.com", Reason = "Express Buff & Shine", Date = new DateTime(2026, 10, 30), Start = new TimeSpan(16, 0, 0), End = new TimeSpan(16, 30, 0), Status = AppointmentStatus.Approved, Creator = "Aarav Malhotra" },

            // =========================================================================
            // WEEK VIEW: SATURDAY, OCT 31, 2026
            // =========================================================================
            new { AptNum = "APT-20261031-1001", ProviderId = ananya.Id, CustEmail = "neha.verma@gmail.com", Reason = "Gold Radiance Facial", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Approved, Creator = "Neha Verma" },
            new { AptNum = "APT-20261031-1002", ProviderId = ananya.Id, CustEmail = "priya.singh@gmail.com", Reason = "Under-Eye Revival Treatment", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(13, 30, 0), End = new TimeSpan(14, 0, 0), Status = AppointmentStatus.Booked, Creator = "Priya Singh" },
            new { AptNum = "APT-20261031-1003", ProviderId = ananya.Id, CustEmail = "pooja.iyer@gmail.com", Reason = "Bridal Glow Prep Facial", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Approved, Creator = "Pooja Iyer" },
            new { AptNum = "APT-20261031-2001", ProviderId = rajat.Id, CustEmail = "vikram.saini@gmail.com", Reason = "VIP Nail Art & Care", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Approved, Creator = "Vikram Saini" },
            new { AptNum = "APT-20261031-2002", ProviderId = rajat.Id, CustEmail = "meera.nair@gmail.com", Reason = "Glitter Ombre Gel Nails", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(12, 30, 0), End = new TimeSpan(13, 0, 0), Status = AppointmentStatus.Booked, Creator = "Meera Nair" },
            new { AptNum = "APT-20261031-2003", ProviderId = rajat.Id, CustEmail = "amit.sharma@gmail.com", Reason = "Executive Hand Grooming", Date = new DateTime(2026, 10, 31), Start = new TimeSpan(15, 30, 0), End = new TimeSpan(16, 0, 0), Status = AppointmentStatus.Approved, Creator = "Amit Sharma" },

            // =========================================================================
            // MONTH VIEW: EARLIER OCTOBER 2026
            // =========================================================================
            new { AptNum = "APT-20261005-1001", ProviderId = ananya.Id, CustEmail = "amit.sharma@gmail.com", Reason = "Hydra-Facial Consultation", Date = new DateTime(2026, 10, 5), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Completed, Creator = "Amit Sharma" },
            new { AptNum = "APT-20261005-2001", ProviderId = rajat.Id, CustEmail = "vikram.saini@gmail.com", Reason = "Classic Nail Care", Date = new DateTime(2026, 10, 5), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Completed, Creator = "Vikram Saini" },
            new { AptNum = "APT-20261008-1001", ProviderId = ananya.Id, CustEmail = "neha.verma@gmail.com", Reason = "Skin Toning Facial", Date = new DateTime(2026, 10, 8), Start = new TimeSpan(11, 0, 0), End = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Completed, Creator = "Neha Verma" },
            new { AptNum = "APT-20261008-2001", ProviderId = rajat.Id, CustEmail = "pooja.iyer@gmail.com", Reason = "Gel Polish Application", Date = new DateTime(2026, 10, 8), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Completed, Creator = "Pooja Iyer" },
            new { AptNum = "APT-20261012-1001", ProviderId = ananya.Id, CustEmail = "rohit.mehta@gmail.com", Reason = "Deep Pore Extraction", Date = new DateTime(2026, 10, 12), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Completed, Creator = "Rohit Mehta" },
            new { AptNum = "APT-20261012-2001", ProviderId = rajat.Id, CustEmail = "arjun.desai@gmail.com", Reason = "Manicure & Hand Massage", Date = new DateTime(2026, 10, 12), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Completed, Creator = "Arjun Desai" },
            new { AptNum = "APT-20261015-1001", ProviderId = ananya.Id, CustEmail = "priya.singh@gmail.com", Reason = "Oxygen Infusion Glow", Date = new DateTime(2026, 10, 15), Start = new TimeSpan(11, 30, 0), End = new TimeSpan(12, 0, 0), Status = AppointmentStatus.Completed, Creator = "Priya Singh" },
            new { AptNum = "APT-20261015-2001", ProviderId = rajat.Id, CustEmail = "sneha.kapoor@gmail.com", Reason = "Acrylic Nail Infills", Date = new DateTime(2026, 10, 15), Start = new TimeSpan(16, 0, 0), End = new TimeSpan(16, 30, 0), Status = AppointmentStatus.Completed, Creator = "Sneha Kapoor" },
            new { AptNum = "APT-20261019-1001", ProviderId = ananya.Id, CustEmail = "simran.kaur@gmail.com", Reason = "Herbal Skin Therapy", Date = new DateTime(2026, 10, 19), Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Completed, Creator = "Simran Kaur" },
            new { AptNum = "APT-20261019-2001", ProviderId = rajat.Id, CustEmail = "aarav.malhotra@gmail.com", Reason = "Natural Nail Buffing", Date = new DateTime(2026, 10, 19), Start = new TimeSpan(12, 0, 0), End = new TimeSpan(12, 30, 0), Status = AppointmentStatus.Completed, Creator = "Aarav Malhotra" },
            new { AptNum = "APT-20261022-1001", ProviderId = ananya.Id, CustEmail = "kavita.rani@gmail.com", Reason = "Brightening Mask Session", Date = new DateTime(2026, 10, 22), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Completed, Creator = "Kavita Rani" },
            new { AptNum = "APT-20261022-2001", ProviderId = rajat.Id, CustEmail = "meera.nair@gmail.com", Reason = "Designer Nail Art", Date = new DateTime(2026, 10, 22), Start = new TimeSpan(14, 30, 0), End = new TimeSpan(15, 0, 0), Status = AppointmentStatus.Completed, Creator = "Meera Nair" },

            // =========================================================================
            // MONTH VIEW: NOVEMBER 2026
            // =========================================================================
            new { AptNum = "APT-20261102-1001", ProviderId = ananya.Id, CustEmail = "amit.sharma@gmail.com", Reason = "Post-Festival Skin Recovery", Date = new DateTime(2026, 11, 2), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Booked, Creator = "Amit Sharma" },
            new { AptNum = "APT-20261102-2001", ProviderId = rajat.Id, CustEmail = "vikram.saini@gmail.com", Reason = "Gel Overlay Maintenance", Date = new DateTime(2026, 11, 2), Start = new TimeSpan(11, 30, 0), End = new TimeSpan(12, 0, 0), Status = AppointmentStatus.Booked, Creator = "Vikram Saini" },
            new { AptNum = "APT-20261105-1001", ProviderId = ananya.Id, CustEmail = "neha.verma@gmail.com", Reason = "Winter Hydration Facial", Date = new DateTime(2026, 11, 5), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Approved, Creator = "Neha Verma" },
            new { AptNum = "APT-20261105-2001", ProviderId = rajat.Id, CustEmail = "pooja.iyer@gmail.com", Reason = "Autumn Nail Design", Date = new DateTime(2026, 11, 5), Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 30, 0), Status = AppointmentStatus.Approved, Creator = "Pooja Iyer" },
            new { AptNum = "APT-20261109-1001", ProviderId = ananya.Id, CustEmail = "rohit.mehta@gmail.com", Reason = "Vitamin Infusion Therapy", Date = new DateTime(2026, 11, 9), Start = new TimeSpan(11, 0, 0), End = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Booked, Creator = "Rohit Mehta" },
            new { AptNum = "APT-20261109-2001", ProviderId = rajat.Id, CustEmail = "arjun.desai@gmail.com", Reason = "Gentlemans Hand Care", Date = new DateTime(2026, 11, 9), Start = new TimeSpan(14, 0, 0), End = new TimeSpan(14, 30, 0), Status = AppointmentStatus.Booked, Creator = "Arjun Desai" },
            new { AptNum = "APT-20261112-1001", ProviderId = ananya.Id, CustEmail = "priya.singh@gmail.com", Reason = "Collagen Firming Treatment", Date = new DateTime(2026, 11, 12), Start = new TimeSpan(15, 30, 0), End = new TimeSpan(16, 0, 0), Status = AppointmentStatus.Approved, Creator = "Priya Singh" },
            new { AptNum = "APT-20261112-2001", ProviderId = rajat.Id, CustEmail = "sneha.kapoor@gmail.com", Reason = "Luxury Spa Manicure", Date = new DateTime(2026, 11, 12), Start = new TimeSpan(10, 30, 0), End = new TimeSpan(11, 0, 0), Status = AppointmentStatus.Approved, Creator = "Sneha Kapoor" },
            new { AptNum = "APT-20261116-1001", ProviderId = ananya.Id, CustEmail = "simran.kaur@gmail.com", Reason = "Clinical Peeling Session", Date = new DateTime(2026, 11, 16), Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Booked, Creator = "Simran Kaur" },
            new { AptNum = "APT-20261116-2001", ProviderId = rajat.Id, CustEmail = "aarav.malhotra@gmail.com", Reason = "Nail Shaping & Cuticle Oil", Date = new DateTime(2026, 11, 16), Start = new TimeSpan(12, 30, 0), End = new TimeSpan(13, 0, 0), Status = AppointmentStatus.Booked, Creator = "Aarav Malhotra" },
            new { AptNum = "APT-20261120-1001", ProviderId = ananya.Id, CustEmail = "meera.nair@gmail.com", Reason = "Glow & Tone Facial", Date = new DateTime(2026, 11, 20), Start = new TimeSpan(14, 30, 0), End = new TimeSpan(15, 0, 0), Status = AppointmentStatus.Approved, Creator = "Meera Nair" },
            new { AptNum = "APT-20261120-2001", ProviderId = rajat.Id, CustEmail = "kavita.rani@gmail.com", Reason = "Bridal Nail Art Trial", Date = new DateTime(2026, 11, 20), Start = new TimeSpan(16, 0, 0), End = new TimeSpan(16, 30, 0), Status = AppointmentStatus.Approved, Creator = "Kavita Rani" }
        };

        foreach (var item in appointmentsToSeed)
        {
            var alreadyExists = await context.Appointments
                .IgnoreQueryFilters()
                .AnyAsync(a => a.AppointmentNumber == item.AptNum 
                            || (a.ServiceProviderId == item.ProviderId && a.AppointmentDate.Date == item.Date.Date && a.StartTime == item.Start));

            if (!alreadyExists)
            {
                var custId = GetCustomerId(item.CustEmail);
                var appointment = new Appointment
                {
                    TenantId = tenantId,
                    AppointmentNumber = item.AptNum,
                    ServiceProviderId = item.ProviderId,
                    CustomerId = custId,
                    Reason = item.Reason,
                    AppointmentDate = item.Date,
                    StartTime = item.Start,
                    EndTime = item.End,
                    Status = item.Status,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = item.Creator,
                    IsDeleted = false
                };

                await context.Appointments.AddAsync(appointment);
            }
        }

        await context.SaveChangesAsync();
    }
}
