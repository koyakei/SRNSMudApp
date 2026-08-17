using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class ItemReplay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<int>(
            name: "RequestType",
            table: "TaggingRequestContracts",
            type: "int",
            nullable: false,
            defaultValue: 0);

        _ = migrationBuilder.CreateTable(
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

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestReplies_OwnerId",
            table: "TaggingRequestReplies",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestReplies_TaggingRequestEntityId",
            table: "TaggingRequestReplies",
            column: "TaggingRequestEntityId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TaggingRequestReplies");

        _ = migrationBuilder.DropColumn(
            name: "RequestType",
            table: "TaggingRequestContracts");
    }
}