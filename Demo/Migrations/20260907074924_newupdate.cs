using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Migrations
{
    /// <inheritdoc />
    public partial class newupdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "ProductId",
                table: "ModifierGroups",
                type: "nvarchar(10)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "ProductId",
                table: "ModifierGroups",
                type: "nvarchar(10)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)");

            migrationBuilder.AddForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id");
        }
    }
}
