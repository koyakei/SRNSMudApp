using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddRequestItemToTaggingRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Items_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Items_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RequestItemId",
            table: "TaggingRequestContracts");
    }
}