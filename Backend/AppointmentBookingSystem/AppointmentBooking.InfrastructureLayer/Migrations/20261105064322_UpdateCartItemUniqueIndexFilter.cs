using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppointmentBooking.InfrastructureLayer.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCartItemUniqueIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId_ServiceId",
                table: "CartItems");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ServiceId",
                table: "CartItems",
                columns: new[] { "CartId", "ServiceId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId_ServiceId",
                table: "CartItems");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ServiceId",
                table: "CartItems",
                columns: new[] { "CartId", "ServiceId" },
                unique: true);
        }
    }
}
