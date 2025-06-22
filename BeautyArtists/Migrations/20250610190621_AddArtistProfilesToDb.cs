using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistProfilesToDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ArtistsProfiles",
                table: "ArtistsProfiles");

            migrationBuilder.RenameTable(
                name: "ArtistsProfiles",
                newName: "ArtistProfile");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ArtistProfile",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistProfile",
                table: "ArtistProfile",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ArtistServices",
                columns: table => new
                {
                    ArtistProfilesId = table.Column<int>(type: "int", nullable: false),
                    ServicesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistServices", x => new { x.ArtistProfilesId, x.ServicesId });
                    table.ForeignKey(
                        name: "FK_ArtistServices_ArtistProfile_ArtistProfilesId",
                        column: x => x.ArtistProfilesId,
                        principalTable: "ArtistProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtistServices_Services_ServicesId",
                        column: x => x.ServicesId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProfile_UserId",
                table: "ArtistProfile",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistServices_ServicesId",
                table: "ArtistServices",
                column: "ServicesId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistProfile_AspNetUsers_UserId",
                table: "ArtistProfile",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArtistProfile_AspNetUsers_UserId",
                table: "ArtistProfile");

            migrationBuilder.DropTable(
                name: "ArtistServices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArtistProfile",
                table: "ArtistProfile");

            migrationBuilder.DropIndex(
                name: "IX_ArtistProfile_UserId",
                table: "ArtistProfile");

            migrationBuilder.RenameTable(
                name: "ArtistProfile",
                newName: "ArtistsProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ArtistsProfiles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArtistsProfiles",
                table: "ArtistsProfiles",
                column: "Id");
        }
    }
}
