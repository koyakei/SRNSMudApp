using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20261014160000_AddCommentItemToTagWeightLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CommentItemId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TagWeightLedgers_CommentItemId",
            table: "TagWeightLedgers",
            column: "CommentItemId");

        migrationBuilder.AddForeignKey(
            name: "FK_TagWeightLedgers_Items_CommentItemId",
            table: "TagWeightLedgers",
            column: "CommentItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.NoAction);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_TagWeightLedgers_Items_CommentItemId",
            table: "TagWeightLedgers");

        migrationBuilder.DropIndex(
            name: "IX_TagWeightLedgers_CommentItemId",
            table: "TagWeightLedgers");

        migrationBuilder.DropColumn(
            name: "CommentItemId",
            table: "TagWeightLedgers");
    }
}
