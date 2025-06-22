using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioCategoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "PortfolioItems");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "PortfolioItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PortfolioCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_CategoryId",
                table: "PortfolioItems",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioItems_PortfolioCategories_CategoryId",
                table: "PortfolioItems",
                column: "CategoryId",
                principalTable: "PortfolioCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioItems_PortfolioCategories_CategoryId",
                table: "PortfolioItems");

            migrationBuilder.DropTable(
                name: "PortfolioCategories");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_CategoryId",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "PortfolioItems");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "PortfolioItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
