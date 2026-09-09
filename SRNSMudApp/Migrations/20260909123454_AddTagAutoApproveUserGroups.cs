using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260909123454_AddTagAutoApproveUserGroups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TagAutoApproveGroups",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagId = table.Column<int>(type: "int", nullable: false),
                UserGroupId = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagAutoApproveGroups", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagAutoApproveGroups_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagAutoApproveGroups_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TagAutoApproveGroups_UserGroups_UserGroupId",
                    column: x => x.UserGroupId,
                    principalTable: "UserGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TagAutoApproveGroups_OwnerId",
            table: "TagAutoApproveGroups",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_TagAutoApproveGroups_TagId_UserGroupId",
            table: "TagAutoApproveGroups",
            columns: new[] { "TagId", "UserGroupId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TagAutoApproveGroups_UserGroupId",
            table: "TagAutoApproveGroups",
            column: "UserGroupId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TagAutoApproveGroups");
    }
}
