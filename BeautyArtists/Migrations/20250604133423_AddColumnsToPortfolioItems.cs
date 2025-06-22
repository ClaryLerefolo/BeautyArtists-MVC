using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnsToPortfolioItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "PortfolioItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientName",
                table: "PortfolioItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "PortfolioItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "PortfolioItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "PortfolioItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "PortfolioItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "PortfolioItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "ClientName",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "PortfolioItems");
        }
    }
}
