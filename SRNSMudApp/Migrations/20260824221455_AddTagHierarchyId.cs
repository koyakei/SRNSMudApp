using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.SqlServer.Types;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260824221455_AddTagHierarchyId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF SERVERPROPERTY('EngineEdition') NOT IN (5, 8)
BEGIN
    BEGIN TRY
        EXEC sp_configure 'show advanced options', 1;
        RECONFIGURE WITH OVERRIDE;
        EXEC sp_configure 'clr enabled', 1;
        RECONFIGURE WITH OVERRIDE;
    END TRY
    BEGIN CATCH
    END CATCH
END", suppressTransaction: true);

        migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags");

        migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_ParentTagId",
            table: "Tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_TagId",
            table: "Tags");

        migrationBuilder.DropColumn(
            name: "TagId",
            table: "Tags");

        migrationBuilder.AddColumn<SqlHierarchyId>(
            name: "Node",
            table: "Tags",
            type: "hierarchyid",
            nullable: false,
            defaultValue: Microsoft.SqlServer.Types.SqlHierarchyId.GetRoot());

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Node",
            table: "Tags",
            column: "Node");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Tags_Node",
            table: "Tags");

        migrationBuilder.DropColumn(
            name: "Node",
            table: "Tags");

        migrationBuilder.AddColumn<int>(
            name: "TagId",
            table: "Tags",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_TagId",
            table: "Tags",
            column: "TagId");

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags",
            column: "TagId",
            principalTable: "Tags",
            principalColumn: "Id");
    }
}
