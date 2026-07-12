using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioLocationToArtistAndBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocationShared",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StudioAddress",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudioCity",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StudioLatitude",
                table: "ArtistProfiles",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StudioLongitude",
                table: "ArtistProfiles",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudioPostalCode",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudioProvince",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLocationShared",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "StudioAddress",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "StudioCity",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "StudioLatitude",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "StudioLongitude",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "StudioPostalCode",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "StudioProvince",
                table: "ArtistProfiles");
        }
    }
}
