using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class ChangeExperienceToYearsExperienceAddInstagramUrlAndProfilePictureUrlToArtistProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Experience",
                table: "ArtistsProfiles",
                newName: "YearsExperience");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "ArtistsProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "ArtistsProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureUrl",
                table: "ArtistsProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Specialization",
                table: "ArtistsProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullName",
                table: "ArtistsProfiles");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "ArtistsProfiles");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUrl",
                table: "ArtistsProfiles");

            migrationBuilder.DropColumn(
                name: "Specialization",
                table: "ArtistsProfiles");

            migrationBuilder.RenameColumn(
                name: "YearsExperience",
                table: "ArtistsProfiles",
                newName: "Experience");
        }
    }
}
