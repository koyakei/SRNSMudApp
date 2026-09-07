using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260906233300_AddItemReplyNotificationRecipients : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ItemReplyNotificationRecipients",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReplyItemId = table.Column<int>(type: "int", nullable: false),
                RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemReplyNotificationRecipients", x => x.Id);
                table.ForeignKey(
                    name: "FK_ItemReplyNotificationRecipients_AspNetUsers_RecipientUserId",
                    column: x => x.RecipientUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemReplyNotificationRecipients_Items_ReplyItemId",
                    column: x => x.ReplyItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ItemReplyNotificationRecipients_RecipientUserId",
            table: "ItemReplyNotificationRecipients",
            column: "RecipientUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemReplyNotificationRecipients_ReplyItemId_RecipientUserId",
            table: "ItemReplyNotificationRecipients",
            columns: new[] { "ReplyItemId", "RecipientUserId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ItemReplyNotificationRecipients");
    }
}
