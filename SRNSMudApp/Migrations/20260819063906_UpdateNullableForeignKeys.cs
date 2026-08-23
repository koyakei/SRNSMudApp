using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260819063906_UpdateNullableForeignKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true,
            filter: "[RequestItemId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true);
    }
}