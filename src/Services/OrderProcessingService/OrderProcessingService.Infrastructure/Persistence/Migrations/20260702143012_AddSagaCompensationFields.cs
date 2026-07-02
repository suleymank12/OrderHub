using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaCompensationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "OrderProcessingSagaStates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductsToRelease",
                table: "OrderProcessingSagaStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReleasedProductIds",
                table: "OrderProcessingSagaStates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "OrderProcessingSagaStates");

            migrationBuilder.DropColumn(
                name: "ProductsToRelease",
                table: "OrderProcessingSagaStates");

            migrationBuilder.DropColumn(
                name: "ReleasedProductIds",
                table: "OrderProcessingSagaStates");
        }
    }
}
