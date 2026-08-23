using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "EF Migrations の既定のクラス命名規約（タイムスタンププレフィックス）に従うため、リネームしない")]
public partial class _20260819065808_RevertItemNullableForeignKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);
    }
}