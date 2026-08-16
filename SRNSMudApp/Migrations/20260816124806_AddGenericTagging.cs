using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericTagging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagTaggingRequestEntity",
                columns: table => new
                {
                    TaggingRequestEntityId = table.Column<int>(type: "int", nullable: false),
                    TagsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTaggingRequestEntity", x => new { x.TaggingRequestEntityId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_TagTaggingRequestEntity_TaggingRequestContracts_TaggingRequestEntityId",
                        column: x => x.TaggingRequestEntityId,
                        principalTable: "TaggingRequestContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagTaggingRequestEntity_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagTaggingRequestReply",
                columns: table => new
                {
                    TaggingRequestReplyId = table.Column<int>(type: "int", nullable: false),
                    TagsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagTaggingRequestReply", x => new { x.TaggingRequestReplyId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_TagTaggingRequestReply_TaggingRequestReplies_TaggingRequestReplyId",
                        column: x => x.TaggingRequestReplyId,
                        principalTable: "TaggingRequestReplies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagTaggingRequestReply_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagTaggingRequestEntity_TagsId",
                table: "TagTaggingRequestEntity",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_TagTaggingRequestReply_TagsId",
                table: "TagTaggingRequestReply",
                column: "TagsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagTaggingRequestEntity");

            migrationBuilder.DropTable(
                name: "TagTaggingRequestReply");
        }
    }
}
