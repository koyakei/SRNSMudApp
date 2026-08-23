using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "EF Migrations の既定のクラス命名規約（タイムスタンププレフィックス）に従うため、リネームしない")]
public partial class _20260819055203_RemoveNullableProperties : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Items_OfferedTargetItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Tags_OfferedTagId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TimelineEvents_Items_TargetItemId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_TimelineEvents_Tags_TargetTagId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropIndex(
            name: "IX_TimelineEvents_TargetItemId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropIndex(
            name: "IX_TimelineEvents_TargetTagId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedTagId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedTargetItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "TargetItemId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropColumn(
            name: "TargetTagId",
            table: "TimelineEvents");

        _ = migrationBuilder.DropColumn(
            name: "TargetType",
            table: "TimelineEvents");

        _ = migrationBuilder.DropColumn(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "OfferedTagId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "OfferedTargetItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RejectComment",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RejectedAt",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RequesterMessage",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "BurnedAt",
            table: "RightAssets");

        _ = migrationBuilder.AlterColumn<int>(
            name: "PreviousWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "NewWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "TimelineTargetJson",
            table: "TimelineEvents",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<string>(
            name: "Embedding",
            table: "Tags",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<int>(
            name: "TagId",
            table: "Tags",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<string>(
            name: "ContractType",
            table: "TaggingRequestContracts",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(21)",
            oldMaxLength: 21);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "ContractPayloadJson",
            table: "TaggingRequestContracts",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AddColumn<string>(
            name: "RejectionInfoJson",
            table: "TaggingRequestContracts",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AlterColumn<int>(
            name: "TargetTagId",
            table: "RightAssets",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "BurnStatusJson",
            table: "RightAssets",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "ItemKindJson",
            table: "Items",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AlterColumn<string>(
            name: "InvitedByAdminId",
            table: "Invitations",
            type: "nvarchar(450)",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(450)",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "InvitationSourceJson",
            table: "Invitations",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.CreateIndex(
            name: "IX_Tags_TagId",
            table: "Tags",
            column: "TagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations",
            column: "InvitedByAdminId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags",
            column: "TagId",
            principalTable: "Tags",
            principalColumn: "Id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags");

        _ = migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags");

        _ = migrationBuilder.DropIndex(
            name: "IX_Tags_TagId",
            table: "Tags");

        _ = migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "TimelineTargetJson",
            table: "TimelineEvents");

        _ = migrationBuilder.DropColumn(
            name: "TagId",
            table: "Tags");

        _ = migrationBuilder.DropColumn(
            name: "ContractPayloadJson",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RejectionInfoJson",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "BurnStatusJson",
            table: "RightAssets");

        _ = migrationBuilder.DropColumn(
            name: "ItemKindJson",
            table: "Items");

        _ = migrationBuilder.DropColumn(
            name: "InvitationSourceJson",
            table: "Invitations");

        _ = migrationBuilder.AlterColumn<int>(
            name: "PreviousWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "NewWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AddColumn<int>(
            name: "TargetItemId",
            table: "TimelineEvents",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AddColumn<int>(
            name: "TargetTagId",
            table: "TimelineEvents",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "TargetType",
            table: "TimelineEvents",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<string>(
            name: "Embedding",
            table: "Tags",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");

        _ = migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<string>(
            name: "ContractType",
            table: "TaggingRequestContracts",
            type: "nvarchar(21)",
            maxLength: 21,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50);

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AddColumn<int>(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AddColumn<int>(
            name: "OfferedTagId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AddColumn<int>(
            name: "OfferedTargetItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "RejectComment",
            table: "TaggingRequestContracts",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        _ = migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RejectedAt",
            table: "TaggingRequestContracts",
            type: "datetimeoffset",
            nullable: true);

        _ = migrationBuilder.AddColumn<string>(
            name: "RequesterMessage",
            table: "TaggingRequestContracts",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        _ = migrationBuilder.AddColumn<int>(
            name: "TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "TargetTagId",
            table: "RightAssets",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AddColumn<DateTime>(
            name: "BurnedAt",
            table: "RightAssets",
            type: "datetime2",
            nullable: true);

        _ = migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.AlterColumn<string>(
            name: "InvitedByAdminId",
            table: "Invitations",
            type: "nvarchar(450)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(450)");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetItemId",
            table: "TimelineEvents",
            column: "TargetItemId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetTagId",
            table: "TimelineEvents",
            column: "TargetTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTagId",
            table: "TaggingRequestContracts",
            column: "OfferedTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTargetItemId",
            table: "TaggingRequestContracts",
            column: "OfferedTargetItemId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true,
            filter: "[RequestItemId] IS NOT NULL");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations",
            column: "InvitedByAdminId",
            principalTable: "AspNetUsers",
            principalColumn: "Id");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Items_OfferedTargetItemId",
            table: "TaggingRequestContracts",
            column: "OfferedTargetItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId",
            principalTable: "PublicTradeOffers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId",
            principalTable: "RightAssets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Tags_OfferedTagId",
            table: "TaggingRequestContracts",
            column: "OfferedTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId",
            principalTable: "Tags",
            principalColumn: "Id");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TimelineEvents_Items_TargetItemId",
            table: "TimelineEvents",
            column: "TargetItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        _ = migrationBuilder.AddForeignKey(
            name: "FK_TimelineEvents_Tags_TargetTagId",
            table: "TimelineEvents",
            column: "TargetTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}