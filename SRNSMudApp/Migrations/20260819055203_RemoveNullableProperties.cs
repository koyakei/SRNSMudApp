using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260819055203_RemoveNullableProperties : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations");

        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Items_OfferedTargetItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropForeignKey(
            name: "FK_TaggingRequestContracts_Tags_OfferedTagId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags");

        migrationBuilder.DropForeignKey(
            name: "FK_TimelineEvents_Items_TargetItemId",
            table: "TimelineEvents");

        migrationBuilder.DropForeignKey(
            name: "FK_TimelineEvents_Tags_TargetTagId",
            table: "TimelineEvents");

        migrationBuilder.DropIndex(
            name: "IX_TimelineEvents_TargetItemId",
            table: "TimelineEvents");

        migrationBuilder.DropIndex(
            name: "IX_TimelineEvents_TargetTagId",
            table: "TimelineEvents");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedTagId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_OfferedTargetItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "TargetItemId",
            table: "TimelineEvents");

        migrationBuilder.DropColumn(
            name: "TargetTagId",
            table: "TimelineEvents");

        migrationBuilder.DropColumn(
            name: "TargetType",
            table: "TimelineEvents");

        migrationBuilder.DropColumn(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "OfferedTagId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "OfferedTargetItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "RejectComment",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "RejectedAt",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "RequesterMessage",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "TargetPublicTradeOfferId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "BurnedAt",
            table: "RightAssets");

        migrationBuilder.AlterColumn<int>(
            name: "PreviousWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "NewWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TimelineTargetJson",
            table: "TimelineEvents",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Embedding",
            table: "Tags",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TagId",
            table: "Tags",
            type: "int",
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "ContractType",
            table: "TaggingRequestContracts",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(21)",
            oldMaxLength: 21);

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContractPayloadJson",
            table: "TaggingRequestContracts",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "RejectionInfoJson",
            table: "TaggingRequestContracts",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<int>(
            name: "TargetTagId",
            table: "RightAssets",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BurnStatusJson",
            table: "RightAssets",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ItemKindJson",
            table: "Items",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<string>(
            name: "InvitedByAdminId",
            table: "Invitations",
            type: "nvarchar(450)",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(450)",
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InvitationSourceJson",
            table: "Invitations",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_TagId",
            table: "Tags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations",
            column: "InvitedByAdminId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags",
            column: "TagId",
            principalTable: "Tags",
            principalColumn: "Id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations");

        migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags");

        migrationBuilder.DropForeignKey(
            name: "FK_Tags_Tags_TagId",
            table: "Tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_TagId",
            table: "Tags");

        migrationBuilder.DropIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "TimelineTargetJson",
            table: "TimelineEvents");

        migrationBuilder.DropColumn(
            name: "TagId",
            table: "Tags");

        migrationBuilder.DropColumn(
            name: "ContractPayloadJson",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "RejectionInfoJson",
            table: "TaggingRequestContracts");

        migrationBuilder.DropColumn(
            name: "BurnStatusJson",
            table: "RightAssets");

        migrationBuilder.DropColumn(
            name: "ItemKindJson",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "InvitationSourceJson",
            table: "Invitations");

        migrationBuilder.AlterColumn<int>(
            name: "PreviousWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "NewWeight",
            table: "TimelineEvents",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddColumn<int>(
            name: "TargetItemId",
            table: "TimelineEvents",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TargetTagId",
            table: "TimelineEvents",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TargetType",
            table: "TimelineEvents",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<int>(
            name: "SourceId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "ParentTagId",
            table: "Tags",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<string>(
            name: "Embedding",
            table: "Tags",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");

        migrationBuilder.AlterColumn<int>(
            name: "RequestItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<string>(
            name: "ContractType",
            table: "TaggingRequestContracts",
            type: "nvarchar(21)",
            maxLength: 21,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50);

        migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddColumn<int>(
            name: "OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OfferedTagId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OfferedTargetItemId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RejectComment",
            table: "TaggingRequestContracts",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RejectedAt",
            table: "TaggingRequestContracts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RequesterMessage",
            table: "TaggingRequestContracts",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "TargetTagId",
            table: "RightAssets",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddColumn<DateTime>(
            name: "BurnedAt",
            table: "RightAssets",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "ParentItemId",
            table: "Items",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<string>(
            name: "InvitedByAdminId",
            table: "Invitations",
            type: "nvarchar(450)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(450)");

        migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetItemId",
            table: "TimelineEvents",
            column: "TargetItemId");

        migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetTagId",
            table: "TimelineEvents",
            column: "TargetTagId");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTagId",
            table: "TaggingRequestContracts",
            column: "OfferedTagId");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTargetItemId",
            table: "TaggingRequestContracts",
            column: "OfferedTargetItemId");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestItemId",
            table: "TaggingRequestContracts",
            column: "RequestItemId",
            unique: true,
            filter: "[RequestItemId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId");

        migrationBuilder.AddForeignKey(
            name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
            table: "Invitations",
            column: "InvitedByAdminId",
            principalTable: "AspNetUsers",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Items_OfferedTargetItemId",
            table: "TaggingRequestContracts",
            column: "OfferedTargetItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_PublicTradeOffers_TargetPublicTradeOfferId",
            table: "TaggingRequestContracts",
            column: "TargetPublicTradeOfferId",
            principalTable: "PublicTradeOffers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_RightAssets_OfferedRewardAssetId",
            table: "TaggingRequestContracts",
            column: "OfferedRewardAssetId",
            principalTable: "RightAssets",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_TaggingRequestContracts_Tags_OfferedTagId",
            table: "TaggingRequestContracts",
            column: "OfferedTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_Tags_ParentTagId",
            table: "Tags",
            column: "ParentTagId",
            principalTable: "Tags",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_TimelineEvents_Items_TargetItemId",
            table: "TimelineEvents",
            column: "TargetItemId",
            principalTable: "Items",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_TimelineEvents_Tags_TargetTagId",
            table: "TimelineEvents",
            column: "TargetTagId",
            principalTable: "Tags",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
