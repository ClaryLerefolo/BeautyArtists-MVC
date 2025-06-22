using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class RemoveArtistIdFromService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Services_AspNetUsers_ArtistId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Services_ArtistId",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "ArtistId",
                table: "Services");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtistId",
                table: "Services",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Services_ArtistId",
                table: "Services",
                column: "ArtistId");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_AspNetUsers_ArtistId",
                table: "Services",
                column: "ArtistId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
