using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "EF Migrations の既定のクラス命名規約（タイムスタンププレフィックス）に従うため、リネームしない")]
public partial class _20260819061846_RevertParentTagIdToNullable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);
    }
}