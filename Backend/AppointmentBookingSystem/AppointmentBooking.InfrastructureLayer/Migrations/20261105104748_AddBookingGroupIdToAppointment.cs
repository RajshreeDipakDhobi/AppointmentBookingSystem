using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentBooking.InfrastructureLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingGroupIdToAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingGroupId",
                table: "Appointments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookingGroupId",
                table: "Appointments",
                column: "BookingGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_BookingGroupId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "BookingGroupId",
                table: "Appointments");
        }
    }
}
