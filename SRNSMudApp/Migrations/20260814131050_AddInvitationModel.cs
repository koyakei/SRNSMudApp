using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddInvitationModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "Invitations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                InvitationCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsUsed = table.Column<bool>(type: "bit", nullable: false),
                InvitedByAdminId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_Invitations", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_Invitations_AspNetUsers_InvitedByAdminId",
                    column: x => x.InvitedByAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id");
                _ = table.ForeignKey(
                    name: "FK_Invitations_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_Invitations_InvitedByAdminId",
            table: "Invitations",
            column: "InvitedByAdminId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_Invitations_OwnerId",
            table: "Invitations",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "Invitations");
    }
}