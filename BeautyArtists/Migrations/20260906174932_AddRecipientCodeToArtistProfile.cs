using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientCodeToArtistProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientCode",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientCode",
                table: "ArtistProfiles");
        }
    }
}
