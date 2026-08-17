using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddGenericTagging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "TagTaggingRequestEntity",
            columns: table => new
            {
                TaggingRequestEntityId = table.Column<int>(type: "int", nullable: false),
                TagsId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagTaggingRequestEntity", x => new { x.TaggingRequestEntityId, x.TagsId });
                _ = table.ForeignKey(
                    name: "FK_TagTaggingRequestEntity_TaggingRequestContracts_TaggingRequestEntityId",
                    column: x => x.TaggingRequestEntityId,
                    principalTable: "TaggingRequestContracts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                _ = table.ForeignKey(
                    name: "FK_TagTaggingRequestEntity_Tags_TagsId",
                    column: x => x.TagsId,
                    principalTable: "Tags",
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
            name: "IX_TagTaggingRequestEntity_TagsId",
            table: "TagTaggingRequestEntity",
            column: "TagsId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagTaggingRequestReply_TagsId",
            table: "TagTaggingRequestReply",
            column: "TagsId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TagTaggingRequestEntity");

        _ = migrationBuilder.DropTable(
            name: "TagTaggingRequestReply");
    }
}