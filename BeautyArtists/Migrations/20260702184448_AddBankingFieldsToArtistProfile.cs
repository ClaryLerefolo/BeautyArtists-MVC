using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyInRedAndGold.Migrations
{
    /// <inheritdoc />
    public partial class AddBankingFieldsToArtistProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountHolderName",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BankAccountVerifiedDate",
                table: "ArtistProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBankAccountVerified",
                table: "ArtistProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubaccountCode",
                table: "ArtistProfiles",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountHolderName",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "BankAccountVerifiedDate",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "IsBankAccountVerified",
                table: "ArtistProfiles");

            migrationBuilder.DropColumn(
                name: "SubaccountCode",
                table: "ArtistProfiles");
        }
    }
}
