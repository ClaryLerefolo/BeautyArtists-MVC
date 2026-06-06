using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationTypeToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HouseCallAddress",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Bookings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Bookings",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectLocationType",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportCost",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HouseCallAddress",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "SelectLocationType",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "TransportCost",
                table: "Bookings");
        }
    }
}
