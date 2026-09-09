using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260909121002_AddUserGroupAndTagDelegation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AutoApproveUserGroupId",
            table: "Tags",
            type: "int",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "UserGroups",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserGroups", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserGroups_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserGroupMembers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserGroupId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserGroupMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserGroupMembers_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserGroupMembers_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserGroupMembers_UserGroups_UserGroupId",
                    column: x => x.UserGroupId,
                    principalTable: "UserGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_AutoApproveUserGroupId",
            table: "Tags",
            column: "AutoApproveUserGroupId");

        migrationBuilder.CreateIndex(
            name: "IX_UserGroupMembers_OwnerId",
            table: "UserGroupMembers",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_UserGroupMembers_UserGroupId_UserId",
            table: "UserGroupMembers",
            columns: new[] { "UserGroupId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserGroupMembers_UserId",
            table: "UserGroupMembers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserGroups_OwnerId",
            table: "UserGroups",
            column: "OwnerId");

        migrationBuilder.AddForeignKey(
            name: "FK_Tags_UserGroups_AutoApproveUserGroupId",
            table: "Tags",
            column: "AutoApproveUserGroupId",
            principalTable: "UserGroups",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Tags_UserGroups_AutoApproveUserGroupId",
            table: "Tags");

        migrationBuilder.DropTable(
            name: "UserGroupMembers");

        migrationBuilder.DropTable(
            name: "UserGroups");

        migrationBuilder.DropIndex(
            name: "IX_Tags_AutoApproveUserGroupId",
            table: "Tags");

        migrationBuilder.DropColumn(
            name: "AutoApproveUserGroupId",
            table: "Tags");
    }
}
