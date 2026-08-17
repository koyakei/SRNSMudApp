using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations
{
    /// <inheritdoc />
    public partial class ItemReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestType",
                table: "TaggingRequestContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TaggingRequestReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaggingRequestEntityId = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaggingRequestReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaggingRequestReplies_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaggingRequestReplies_TaggingRequestContracts_TaggingRequestEntityId",
                        column: x => x.TaggingRequestEntityId,
                        principalTable: "TaggingRequestContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaggingRequestReplies_OwnerId",
                table: "TaggingRequestReplies",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TaggingRequestReplies_TaggingRequestEntityId",
                table: "TaggingRequestReplies",
                column: "TaggingRequestEntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaggingRequestReplies");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "TaggingRequestContracts");
        }
    }
}