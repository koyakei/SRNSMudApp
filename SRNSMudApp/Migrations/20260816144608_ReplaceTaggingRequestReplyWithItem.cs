using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class ReplaceTaggingRequestReplyWithItem : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TagTaggingRequestReply");

        _ = migrationBuilder.DropTable(
            name: "TaggingRequestReplies");

        _ = migrationBuilder.AddColumn<int>(
            name: "TaggingRequestEntityId",
            table: "Items",
            type: "int",
            nullable: true);

        _ = migrationBuilder.CreateIndex(
            name: "IX_Items_TaggingRequestEntityId",
            table: "Items",
            column: "TaggingRequestEntityId");

        _ = migrationBuilder.AddForeignKey(
            name: "FK_Items_TaggingRequestContracts_TaggingRequestEntityId",
            table: "Items",
            column: "TaggingRequestEntityId",
            principalTable: "TaggingRequestContracts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropForeignKey(
            name: "FK_Items_TaggingRequestContracts_TaggingRequestEntityId",
            table: "Items");

        _ = migrationBuilder.DropIndex(
            name: "IX_Items_TaggingRequestEntityId",
            table: "Items");

        _ = migrationBuilder.DropColumn(
            name: "TaggingRequestEntityId",
            table: "Items");

        _ = migrationBuilder.CreateTable(
            name: "TaggingRequestReplies",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                TaggingRequestEntityId = table.Column<int>(type: "int", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TaggingRequestReplies", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestReplies_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestReplies_TaggingRequestContracts_TaggingRequestEntityId",
                    column: x => x.TaggingRequestEntityId,
                    principalTable: "TaggingRequestContracts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
            name: "TagTaggingRequestReply",
            columns: table => new
            {
                TaggingRequestReplyId = table.Column<int>(type: "int", nullable: false),
                TagsId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagTaggingRequestReply", x => new { x.TaggingRequestReplyId, x.TagsId });
                _ = table.ForeignKey(
                    name: "FK_TagTaggingRequestReply_TaggingRequestReplies_TaggingRequestReplyId",
                    column: x => x.TaggingRequestReplyId,
                    principalTable: "TaggingRequestReplies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                _ = table.ForeignKey(
                    name: "FK_TagTaggingRequestReply_Tags_TagsId",
                    column: x => x.TagsId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestReplies_OwnerId",
            table: "TaggingRequestReplies",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestReplies_TaggingRequestEntityId",
            table: "TaggingRequestReplies",
            column: "TaggingRequestEntityId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagTaggingRequestReply_TagsId",
            table: "TagTaggingRequestReply",
            column: "TagsId");
    }
}