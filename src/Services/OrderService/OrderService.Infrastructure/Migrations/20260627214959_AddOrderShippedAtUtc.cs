using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderHub.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShippedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippedAtUtc",
                table: "Orders");
        }
    }
}
