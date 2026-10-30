using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentBooking.InfrastructureLayer.Migrations
{
    /// <inheritdoc />
    public partial class MakeServiceProviderIdNullableInAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ServiceProviderId_AppointmentDate_StartTime",
                table: "Appointments");

            migrationBuilder.AlterColumn<int>(
                name: "ServiceProviderId",
                table: "Appointments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceProviderId_AppointmentDate_StartTime",
                table: "Appointments",
                columns: new[] { "ServiceProviderId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[ServiceProviderId] IS NOT NULL AND [Status] <> 3 AND [Status] <> 4 AND [Status] <> 6 AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments",
                column: "ServiceProviderId",
                principalTable: "ServiceProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ServiceProviderId_AppointmentDate_StartTime",
                table: "Appointments");

            migrationBuilder.AlterColumn<int>(
                name: "ServiceProviderId",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceProviderId_AppointmentDate_StartTime",
                table: "Appointments",
                columns: new[] { "ServiceProviderId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] <> 3 AND [Status] <> 4 AND [Status] <> 6 AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ServiceProviders_ServiceProviderId",
                table: "Appointments",
                column: "ServiceProviderId",
                principalTable: "ServiceProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
