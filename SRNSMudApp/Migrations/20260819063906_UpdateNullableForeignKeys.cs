using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "EF Migrations の既定のクラス命名規約（タイムスタンププレフィックス）に従うため、リネームしない")]
public partial class _20260819063906_UpdateNullableForeignKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

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

        _ = migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true);
    }
}