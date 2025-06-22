using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class AddDurationToUserService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Duration",
                table: "UserServices",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "UserServices");
        }
    }
}
