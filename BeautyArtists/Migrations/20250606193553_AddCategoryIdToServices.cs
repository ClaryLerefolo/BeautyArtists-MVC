using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyArtists.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIdToServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CustomDescription",
                table: "UserServices",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PortfolioCategoryId",
                table: "UserServices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserServices_PortfolioCategoryId",
                table: "UserServices",
                column: "PortfolioCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserServices_PortfolioCategories_PortfolioCategoryId",
                table: "UserServices",
                column: "PortfolioCategoryId",
                principalTable: "PortfolioCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserServices_PortfolioCategories_PortfolioCategoryId",
                table: "UserServices");

            migrationBuilder.DropIndex(
                name: "IX_UserServices_PortfolioCategoryId",
                table: "UserServices");

            migrationBuilder.DropColumn(
                name: "PortfolioCategoryId",
                table: "UserServices");

            migrationBuilder.AlterColumn<string>(
                name: "CustomDescription",
                table: "UserServices",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
