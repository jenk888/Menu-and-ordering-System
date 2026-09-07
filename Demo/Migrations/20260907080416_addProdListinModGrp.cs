using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Migrations
{
    /// <inheritdoc />
    public partial class addProdListinModGrp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups");

            migrationBuilder.DropIndex(
                name: "IX_ModifierGroups_ProductId",
                table: "ModifierGroups");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "ModifierGroups");

            migrationBuilder.CreateTable(
                name: "ModifierGroupProduct",
                columns: table => new
                {
                    ModifierGroupsId = table.Column<int>(type: "int", nullable: false),
                    ProductsId = table.Column<string>(type: "nvarchar(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModifierGroupProduct", x => new { x.ModifierGroupsId, x.ProductsId });
                    table.ForeignKey(
                        name: "FK_ModifierGroupProduct_ModifierGroups_ModifierGroupsId",
                        column: x => x.ModifierGroupsId,
                        principalTable: "ModifierGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModifierGroupProduct_Products_ProductsId",
                        column: x => x.ProductsId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModifierGroupProduct_ProductsId",
                table: "ModifierGroupProduct",
                column: "ProductsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModifierGroupProduct");

            migrationBuilder.AddColumn<string>(
                name: "ProductId",
                table: "ModifierGroups",
                type: "nvarchar(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ModifierGroups_ProductId",
                table: "ModifierGroups",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_ModifierGroups_Products_ProductId",
                table: "ModifierGroups",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
