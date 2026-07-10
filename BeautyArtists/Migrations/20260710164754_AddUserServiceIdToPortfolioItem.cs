using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddUserServiceIdToPortfolioItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserServiceId",
                table: "PortfolioItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioItems_UserServiceId",
                table: "PortfolioItems",
                column: "UserServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_PortfolioItems_UserServices_UserServiceId",
                table: "PortfolioItems",
                column: "UserServiceId",
                principalTable: "UserServices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortfolioItems_UserServices_UserServiceId",
                table: "PortfolioItems");

            migrationBuilder.DropIndex(
                name: "IX_PortfolioItems_UserServiceId",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "UserServiceId",
                table: "PortfolioItems");
        }
    }
}
