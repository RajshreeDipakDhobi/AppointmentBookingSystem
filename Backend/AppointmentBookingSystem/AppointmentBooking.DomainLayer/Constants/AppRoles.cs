namespace AppointmentBooking.DomainLayer.Constants;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string ServiceProvider = "ServiceProvider";
    public const string Customer = "Customer";

    // Combined Role Policies
    public const string AdminOrSuperAdmin = "Admin,SuperAdmin";
    public const string AllStaff = "SuperAdmin,Admin,ServiceProvider";
    public const string AuthenticatedUser = "SuperAdmin,Admin,ServiceProvider,Customer";
}
