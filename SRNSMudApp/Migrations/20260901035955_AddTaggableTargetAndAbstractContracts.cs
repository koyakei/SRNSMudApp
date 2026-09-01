using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260901035955_AddTaggableTargetAndAbstractContracts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Items_TargetItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.RenameColumn(
            name: "TargetItemId",
            table: "TaggingRequestContracts",
            newName: "TargetId");

        migrationBuilder.RenameIndex(
            name: "IX_TaggingRequestContracts_TargetItemId",
            table: "TaggingRequestContracts",
            newName: "IX_TaggingRequestContracts_TargetId");

        migrationBuilder.CreateTable(
            name: "TaggableTargets",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaggableTargets", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaggableTargets_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<int>(
            name: "TagTargetId",
            table: "TagEdges",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TagTargetId",
            table: "Items",
            type: "int",
            nullable: true);

        // 既存データのバックフィル
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Items)
BEGIN
    CREATE TABLE #ItemMapping (TagTargetId INT, ItemId INT);

    MERGE INTO TaggableTargets AS target
    USING (SELECT Id, OwnerId, CreatedDate, UpdatedDate FROM Items) AS source
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (TargetType, OwnerId, CreatedDate, UpdatedDate)
        VALUES ('Item', source.OwnerId, source.CreatedDate, source.UpdatedDate)
    OUTPUT inserted.Id, source.Id INTO #ItemMapping (TagTargetId, ItemId);

    UPDATE Items
    SET TagTargetId = m.TagTargetId
    FROM #ItemMapping m
    WHERE Items.Id = m.ItemId;

    UPDATE TaggingRequestContracts
    SET TargetId = m.TagTargetId
    FROM #ItemMapping m
    WHERE TaggingRequestContracts.TargetId = m.ItemId;

    DROP TABLE #ItemMapping;
END

IF EXISTS (SELECT 1 FROM TagEdges)
BEGIN
    CREATE TABLE #EdgeMapping (TagTargetId INT, EdgeId INT);

    MERGE INTO TaggableTargets AS target
    USING (SELECT Id, OwnerId, CreatedDate, UpdatedDate FROM TagEdges) AS source
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (TargetType, OwnerId, CreatedDate, UpdatedDate)
        VALUES ('TagEdge', source.OwnerId, source.CreatedDate, source.UpdatedDate)
    OUTPUT inserted.Id, source.Id INTO #EdgeMapping (TagTargetId, EdgeId);

    UPDATE TagEdges
    SET TagTargetId = m.TagTargetId
    FROM #EdgeMapping m
    WHERE TagEdges.Id = m.EdgeId;

    DROP TABLE #EdgeMapping;
END
");

        migrationBuilder.AlterColumn<int>(
            name: "TagTargetId",
            table: "TagEdges",
            type: "int",
            nullable: false);

        migrationBuilder.AlterColumn<int>(
            name: "TagTargetId",
            table: "Items",
            type: "int",
            nullable: false);

        migrationBuilder.CreateIndex(
            name: "IX_TagEdges_TagTargetId",
            table: "TagEdges",
            column: "TagTargetId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_TagTargetId",
            table: "Items",
            column: "TagTargetId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaggableTargets_OwnerId",
            table: "TaggableTargets",
            column: "OwnerId");

        migrationBuilder.AddForeignKey(
            name: "FK_Items_TaggableTargets_TagTargetId",
            table: "Items",
            column: "TagTargetId",
            principalTable: "TaggableTargets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_TagEdges_TaggableTargets_TagTargetId",
            table: "TagEdges",
            column: "TagTargetId",
            principalTable: "TaggableTargets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_TaggableTargets_TargetId",
            table: "TaggingRequestContracts",
            column: "TargetId",
            principalTable: "TaggableTargets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Items_TaggableTargets_TagTargetId",
            table: "Items");

        migrationBuilder.DropForeignKey(
            name: "FK_TagEdges_TaggableTargets_TagTargetId",
            table: "TagEdges");

        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_TaggableTargets_TargetId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropTable(
            name: "TaggableTargets");

        migrationBuilder.DropIndex(
            name: "IX_TagEdges_TagTargetId",
            table: "TagEdges");

        migrationBuilder.DropIndex(
            name: "IX_Items_TagTargetId",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "TagTargetId",
            table: "TagEdges");

        migrationBuilder.DropColumn(
            name: "TagTargetId",
            table: "Items");

        migrationBuilder.RenameColumn(
            name: "TargetId",
            table: "TaggingRequestContracts",
            newName: "TargetItemId");

        migrationBuilder.RenameIndex(
            name: "IX_TaggingRequestContracts_TargetId",
            table: "TaggingRequestContracts",
            newName: "IX_TaggingRequestContracts_TargetItemId");

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Items_TargetItemId",
            table: "TaggingRequestContracts",
            column: "TargetItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
