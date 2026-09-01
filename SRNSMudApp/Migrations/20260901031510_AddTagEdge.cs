using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260901031510_AddTagEdge : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TagEdges",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceTagId = table.Column<int>(type: "int", nullable: false),
                TargetTagId = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagEdges", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagEdges_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagEdges_Tags_SourceTagId",
                    column: x => x.SourceTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagEdges_Tags_TargetTagId",
                    column: x => x.TargetTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TagEdgeTagAttachments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagEdgeId = table.Column<int>(type: "int", nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false),
                Weight = table.Column<int>(type: "int", nullable: false),
                ConsumedRightAssetId = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagEdgeTagAttachments", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagEdgeTagAttachments_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagEdgeTagAttachments_RightAssets_ConsumedRightAssetId",
                    column: x => x.ConsumedRightAssetId,
                    principalTable: "RightAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagEdgeTagAttachments_TagEdges_TagEdgeId",
                    column: x => x.TagEdgeId,
                    principalTable: "TagEdges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TagEdgeTagAttachments_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TagEdges_OwnerId_SourceTagId_TargetTagId",
            table: "TagEdges",
            columns: new[] { "OwnerId", "SourceTagId", "TargetTagId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TagEdges_SourceTagId",
            table: "TagEdges",
            column: "SourceTagId");

        migrationBuilder.CreateIndex(
            name: "IX_TagEdges_TargetTagId",
            table: "TagEdges",
            column: "TargetTagId");

        migrationBuilder.CreateIndex(
            name: "IX_TagEdgeTagAttachments_ConsumedRightAssetId",
            table: "TagEdgeTagAttachments",
            column: "ConsumedRightAssetId");

        migrationBuilder.CreateIndex(
            name: "IX_TagEdgeTagAttachments_OwnerId",
            table: "TagEdgeTagAttachments",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_TagEdgeTagAttachments_TagEdgeId_TagId",
            table: "TagEdgeTagAttachments",
            columns: new[] { "TagEdgeId", "TagId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TagEdgeTagAttachments_TagId",
            table: "TagEdgeTagAttachments",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TagEdgeTagAttachments");

        migrationBuilder.DropTable(
            name: "TagEdges");
    }
}
