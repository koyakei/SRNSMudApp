using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddAsRequestOfNavigation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true,
            filter: "[RequestItemId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId");
    }
}