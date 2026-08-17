using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddItemReplies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_Items_ParentItemId",
            table: "Items",
            column: "ParentItemId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Items_Items_ParentItemId",
            table: "Items",
            column: "ParentItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_Items_Items_ParentItemId",
            table: "Items");

        _ = migrationBuilder.DropIndex(
            name: "IX_Items_ParentItemId",
            table: "Items");

        _ = migrationBuilder.DropColumn(
            name: "ParentItemId",
            table: "Items");
    }
}