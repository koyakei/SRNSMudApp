using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddPublicTradeOffer : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<int>(
            name: "TargetTagId",
            table: "RightAssets",
            type: "int",
            nullable: true);

        _ = migrationBuilder.CreateTable(
            name: "PublicTradeOffers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OfferedTagId = table.Column<int>(type: "int", nullable: false),
                RequiredAssetAmount = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_PublicTradeOffers", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_PublicTradeOffers_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_PublicTradeOffers_Tags_OfferedTagId",
                    column: x => x.OfferedTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_RightAssets_TargetTagId",
            table: "RightAssets",
            column: "TargetTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_PublicTradeOffers_OfferedTagId",
            table: "PublicTradeOffers",
            column: "OfferedTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_PublicTradeOffers_OwnerId",
            table: "PublicTradeOffers",
            column: "OwnerId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_RightAssets_Tags_TargetTagId",
            table: "RightAssets",
            column: "TargetTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId",
            principalTable: "PublicTradeOffers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_RightAssets_Tags_TargetTagId",
            table: "RightAssets");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropTable(
            name: "PublicTradeOffers");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_RightAssets_TargetTagId",
            table: "RightAssets");

        _ = migrationBuilder.DropColumn(
            name: "TargetTagId",
            table: "RightAssets");
    }
}