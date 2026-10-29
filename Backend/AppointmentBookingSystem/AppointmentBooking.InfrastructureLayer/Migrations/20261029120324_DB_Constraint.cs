using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentBooking.InfrastructureLayer.Migrations
{
    /// <inheritdoc />
    public partial class DB_Constraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Users_CustomerId",
                table: "Appointments");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments",
                column: "ServiceProviderId",
                principalTable: "ServiceProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Users_CustomerId",
                table: "Appointments",
                column: "CustomerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Users_CustomerId",
                table: "Appointments");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments",
                column: "ServiceProviderId",
                principalTable: "ServiceProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Users_CustomerId",
                table: "Appointments",
                column: "CustomerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
