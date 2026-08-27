using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260827120233_AddTargetTagIdToTagWeightLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TargetTagId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE TagWeightLedgers SET TargetTagId = (SELECT TargetTagId FROM TagRelationToTags WHERE TagRelationToTags.Id = TagWeightLedgers.SourceId) WHERE SourceId IS NOT NULL AND TargetTagId IS NULL AND SourceType LIKE 'TagRelationToTag%';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TargetTagId",
            table: "TagWeightLedgers");
    }
}
