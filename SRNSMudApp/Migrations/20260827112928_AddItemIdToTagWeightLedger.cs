using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260827112928_AddItemIdToTagWeightLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ItemId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE TagWeightLedgers SET ItemId = (SELECT ItemId FROM TagRelations WHERE TagRelations.Id = TagWeightLedgers.SourceId) WHERE SourceId IS NOT NULL AND ItemId IS NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ItemId",
            table: "TagWeightLedgers");
    }
}
