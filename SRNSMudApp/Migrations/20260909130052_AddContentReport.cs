using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260909130052_AddContentReport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ContentReports",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TargetType = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: true),
                TagId = table.Column<int>(type: "int", nullable: true),
                TargetContentSnapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ResolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                HandledByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                HandledDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContentReports", x => x.Id);
                table.ForeignKey(
                    name: "FK_ContentReports_AspNetUsers_HandledByAdminId",
                    column: x => x.HandledByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContentReports_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ContentReports_Items_ItemId",
                    column: x => x.ItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_ContentReports_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_CreatedDate",
            table: "ContentReports",
            column: "CreatedDate");

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_HandledByAdminId",
            table: "ContentReports",
            column: "HandledByAdminId");

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_ItemId",
            table: "ContentReports",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_OwnerId",
            table: "ContentReports",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_Status",
            table: "ContentReports",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ContentReports_TagId",
            table: "ContentReports",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ContentReports");
    }
}
