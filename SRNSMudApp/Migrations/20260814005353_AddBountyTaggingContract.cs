using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddBountyTaggingContract : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<int>(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId",
            principalTable: "RightAssets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts");
    }
}