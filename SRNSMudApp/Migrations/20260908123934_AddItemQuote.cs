using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260908123934_AddItemQuote : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "QuotedItemId",
            table: "Items",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_QuotedItemId",
            table: "Items",
            column: "QuotedItemId");

        migrationBuilder.AddForeignKey(
            name: "FK_Items_Items_QuotedItemId",
            table: "Items",
            column: "QuotedItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Items_Items_QuotedItemId",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Items_QuotedItemId",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "QuotedItemId",
            table: "Items");
    }
}
