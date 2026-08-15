using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class MergeAuditLogIntoLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TagRelationWeightAuditLogs");

        _ = migrationBuilder.DropTable(
            name: "TagWeightAuditLogs");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        _ = migrationBuilder.AddColumn<bool>(
            name: "IsOwnerAction",
            table: "TagWeightLedgers",
            type: "bit",
            nullable: false,
            defaultValue: false);

        _ = migrationBuilder.AddColumn<int>(
            name: "NewWeight",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0);

        _ = migrationBuilder.AddColumn<int>(
            name: "PreviousWeight",
            table: "TagWeightLedgers",
            type: "int",
            nullable: false,
            defaultValue: 0);

        _ = migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "TagWeightLedgers",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "");

        _ = migrationBuilder.AddColumn<string>(
            name: "TagNameSnapshot",
            table: "TagWeightLedgers",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropColumn(
            name: "IsOwnerAction",
            table: "TagWeightLedgers");

        _ = migrationBuilder.DropColumn(
            name: "NewWeight",
            table: "TagWeightLedgers");

        _ = migrationBuilder.DropColumn(
            name: "PreviousWeight",
            table: "TagWeightLedgers");

        _ = migrationBuilder.DropColumn(
            name: "Reason",
            table: "TagWeightLedgers");

        _ = migrationBuilder.DropColumn(
            name: "TagNameSnapshot",
            table: "TagWeightLedgers");

        _ = migrationBuilder.AlterColumn<int>(
            name: "ConsumedRightAssetId",
            table: "TagWeightLedgers",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        _ = migrationBuilder.CreateTable(
            name: "TagRelationWeightAuditLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Delta = table.Column<int>(type: "int", nullable: false),
                ExecutorUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                IsOwnerAction = table.Column<bool>(type: "bit", nullable: false),
                ItemIdSnapshot = table.Column<int>(type: "int", nullable: false),
                NewWeight = table.Column<int>(type: "int", nullable: false),
                PreviousWeight = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                TagIdSnapshot = table.Column<int>(type: "int", nullable: false),
                TagRelationId = table.Column<int>(type: "int", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagRelationWeightAuditLogs", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TagRelationWeightAuditLogs_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "TagWeightAuditLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Delta = table.Column<int>(type: "int", nullable: false),
                ExecutorUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                IsOwnerAction = table.Column<bool>(type: "bit", nullable: false),
                NewWeight = table.Column<int>(type: "int", nullable: false),
                PreviousWeight = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false),
                TagNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagWeightAuditLogs", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TagWeightAuditLogs_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagRelationWeightAuditLogs_OwnerId",
            table: "TagRelationWeightAuditLogs",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightAuditLogs_OwnerId",
            table: "TagWeightAuditLogs",
            column: "OwnerId");
    }
}