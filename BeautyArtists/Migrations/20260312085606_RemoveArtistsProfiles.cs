using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class RemoveArtistsProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtistProfile_AspNetUsers_UserId",
                table: "ArtistProfile");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtistServices_ArtistProfile_ArtistProfilesId",
                table: "ArtistServices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArtistProfile",
                table: "ArtistProfile");

            migrationBuilder.RenameTable(
                name: "ArtistProfile",
                newName: "ArtistProfiles");

            migrationBuilder.RenameIndex(
                name: "IX_ArtistProfile_UserId",
                table: "ArtistProfiles",
                newName: "IX_ArtistProfiles_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistProfiles",
                table: "ArtistProfiles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistProfiles_AspNetUsers_UserId",
                table: "ArtistProfiles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistServices_ArtistProfiles_ArtistProfilesId",
                table: "ArtistServices",
                column: "ArtistProfilesId",
                principalTable: "ArtistProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtistProfiles_AspNetUsers_UserId",
                table: "ArtistProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtistServices_ArtistProfiles_ArtistProfilesId",
                table: "ArtistServices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArtistProfiles",
                table: "ArtistProfiles");

            migrationBuilder.RenameTable(
                name: "ArtistProfiles",
                newName: "ArtistProfile");

            migrationBuilder.RenameIndex(
                name: "IX_ArtistProfiles_UserId",
                table: "ArtistProfile",
                newName: "IX_ArtistProfile_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistProfile",
                table: "ArtistProfile",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistProfile_AspNetUsers_UserId",
                table: "ArtistProfile",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistServices_ArtistProfile_ArtistProfilesId",
                table: "ArtistServices",
                column: "ArtistProfilesId",
                principalTable: "ArtistProfile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
