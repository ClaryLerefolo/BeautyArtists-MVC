using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositDeadlineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DepositDueDate",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentReminder1SentAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentReminder2SentAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentReminder3SentAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentWindowOpen",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositDueDate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentReminder1SentAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentReminder2SentAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentReminder3SentAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentWindowOpen",
                table: "Bookings");
        }
    }
}
