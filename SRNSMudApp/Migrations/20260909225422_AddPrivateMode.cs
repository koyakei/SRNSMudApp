using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260909225422_AddPrivateMode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPrivate",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "TargetUserGroupId",
            table: "Items",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DefaultPrivateUserGroupId",
            table: "AspNetUsers",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsPrivateModeDefault",
            table: "AspNetUsers",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Items_IsPrivate",
            table: "Items",
            column: "IsPrivate");

        migrationBuilder.CreateIndex(
            name: "IX_Items_TargetUserGroupId",
            table: "Items",
            column: "TargetUserGroupId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_DefaultPrivateUserGroupId",
            table: "AspNetUsers",
            column: "DefaultPrivateUserGroupId");

        migrationBuilder.AddForeignKey(
            name: "FK_AspNetUsers_UserGroups_DefaultPrivateUserGroupId",
            table: "AspNetUsers",
            column: "DefaultPrivateUserGroupId",
            principalTable: "UserGroups",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Items_UserGroups_TargetUserGroupId",
            table: "Items",
            column: "TargetUserGroupId",
            principalTable: "UserGroups",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_AspNetUsers_UserGroups_DefaultPrivateUserGroupId",
            table: "AspNetUsers");

        migrationBuilder.DropForeignKey(
            name: "FK_Items_UserGroups_TargetUserGroupId",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Items_IsPrivate",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Items_TargetUserGroupId",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_AspNetUsers_DefaultPrivateUserGroupId",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "IsPrivate",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "TargetUserGroupId",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "DefaultPrivateUserGroupId",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "IsPrivateModeDefault",
            table: "AspNetUsers");
    }
}
