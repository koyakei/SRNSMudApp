using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260911121250_Add_TagNameProposal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TagNameProposals",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagId = table.Column<int>(type: "int", nullable: false),
                RequesterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProposedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagNameProposals", x => x.Id);
                table.ForeignKey(
                    name: "FK_TagNameProposals_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagNameProposals_AspNetUsers_OwnerUserId",
                    column: x => x.OwnerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagNameProposals_AspNetUsers_RequesterUserId",
                    column: x => x.RequesterUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TagNameProposals_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TagNameProposals_OwnerId",
            table: "TagNameProposals",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_TagNameProposals_OwnerUserId",
            table: "TagNameProposals",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_TagNameProposals_RequesterUserId",
            table: "TagNameProposals",
            column: "RequesterUserId");

        migrationBuilder.CreateIndex(
            name: "IX_TagNameProposals_Status",
            table: "TagNameProposals",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_TagNameProposals_TagId",
            table: "TagNameProposals",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TagNameProposals");
    }
}
