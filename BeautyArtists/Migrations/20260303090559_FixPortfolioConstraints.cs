using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class FixPortfolioConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioItems_ServiceCategories_ServiceCategoryId",
                table: "PortfolioItems");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_ServiceCategoryId",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "ServiceCategoryId",
                table: "PortfolioItems");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_CategoryId",
                table: "PortfolioItems",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioItems_ServiceCategories_CategoryId",
                table: "PortfolioItems",
                column: "CategoryId",
                principalTable: "ServiceCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioItems_ServiceCategories_CategoryId",
                table: "PortfolioItems");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_CategoryId",
                table: "PortfolioItems");

            migrationBuilder.AddColumn<int>(
                name: "ServiceCategoryId",
                table: "PortfolioItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_ServiceCategoryId",
                table: "PortfolioItems",
                column: "ServiceCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioItems_ServiceCategories_ServiceCategoryId",
                table: "PortfolioItems",
                column: "ServiceCategoryId",
                principalTable: "ServiceCategories",
                principalColumn: "Id");
        }
    }
}
