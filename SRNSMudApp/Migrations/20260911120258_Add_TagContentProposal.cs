using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260911120258_Add_TagContentProposal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TagContentProposals",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagId = table.Column<int>(type: "int", nullable: false),
                RequesterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProposedContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagContentProposals", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagContentProposals_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagContentProposals_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagContentProposals_AspNetUsers_RequesterUserId",
                    column: x => x.RequesterUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagContentProposals_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TagContentProposals_OwnerId",
            table: "TagContentProposals",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_TagContentProposals_OwnerUserId",
            table: "TagContentProposals",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_TagContentProposals_RequesterUserId",
            table: "TagContentProposals",
            column: "RequesterUserId");

        migrationBuilder.CreateIndex(
            name: "IX_TagContentProposals_Status",
            table: "TagContentProposals",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_TagContentProposals_TagId",
            table: "TagContentProposals",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TagContentProposals");
    }
}
