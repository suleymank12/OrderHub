using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderHub.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingSweepIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_CreatedAtUtc",
                table: "Orders");
        }
    }
}
