using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20261016150000_AddRequesterQuoteItemToSplitRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RequesterQuoteItemId",
            table: "ItemSplitRequests",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_RequesterQuoteItemId",
            table: "ItemSplitRequests",
            column: "RequesterQuoteItemId");

        migrationBuilder.AddForeignKey(
            name: "FK_ItemSplitRequests_Items_RequesterQuoteItemId",
            table: "ItemSplitRequests",
            column: "RequesterQuoteItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ItemSplitRequests_Items_RequesterQuoteItemId",
            table: "ItemSplitRequests");

        migrationBuilder.DropIndex(
            name: "IX_ItemSplitRequests_RequesterQuoteItemId",
            table: "ItemSplitRequests");

        migrationBuilder.DropColumn(
            name: "RequesterQuoteItemId",
            table: "ItemSplitRequests");
    }
}

