using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260824235206_AddRootTagConstraint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_Tags_RootOnlyForUniversalTag",
            table: "Tags",
            sql: "[Name] = N'全て∀' OR [Node] <> hierarchyid::GetRoot()");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Tags_RootOnlyForUniversalTag",
            table: "Tags");
    }
}