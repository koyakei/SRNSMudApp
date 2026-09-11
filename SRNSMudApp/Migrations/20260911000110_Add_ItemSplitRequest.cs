using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260911000110_Add_ItemSplitRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ItemSplitRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OriginalItemId = table.Column<int>(type: "int", nullable: false),
                RequesterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                SelectedText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedItemId = table.Column<int>(type: "int", nullable: true),
                RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemSplitRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_ItemSplitRequests_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemSplitRequests_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemSplitRequests_AspNetUsers_RequesterUserId",
                    column: x => x.RequesterUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemSplitRequests_Items_CreatedItemId",
                    column: x => x.CreatedItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemSplitRequests_Items_OriginalItemId",
                    column: x => x.OriginalItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_CreatedItemId",
            table: "ItemSplitRequests",
            column: "CreatedItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_OriginalItemId",
            table: "ItemSplitRequests",
            column: "OriginalItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_OwnerId",
            table: "ItemSplitRequests",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_OwnerUserId",
            table: "ItemSplitRequests",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_RequesterUserId",
            table: "ItemSplitRequests",
            column: "RequesterUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemSplitRequests_Status",
            table: "ItemSplitRequests",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ItemSplitRequests");
    }
}
