using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddTaggingRequestEntity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.RenameColumn(
            name: "Weight",
            table: "Tags",
            newName: "CachedWeight");

        _ = migrationBuilder.CreateTable(
            name: "RightAssets",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Amount = table.Column<int>(type: "int", nullable: false),
                IsBurned = table.Column<bool>(type: "bit", nullable: false),
                BurnedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_RightAssets", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_RightAssets_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "TagRelationWeightAuditLogs",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagRelationId = table.Column<int>(type: "int", nullable: false),
                ItemIdSnapshot = table.Column<int>(type: "int", nullable: false),
                TagIdSnapshot = table.Column<int>(type: "int", nullable: false),
                ExecutorUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                IsOwnerAction = table.Column<bool>(type: "bit", nullable: false),
                PreviousWeight = table.Column<int>(type: "int", nullable: false),
                NewWeight = table.Column<int>(type: "int", nullable: false),
                Delta = table.Column<int>(type: "int", nullable: false),
                ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                TagId = table.Column<int>(type: "int", nullable: false),
                TagNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ExecutorUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                IsOwnerAction = table.Column<bool>(type: "bit", nullable: false),
                PreviousWeight = table.Column<int>(type: "int", nullable: false),
                NewWeight = table.Column<int>(type: "int", nullable: false),
                Delta = table.Column<int>(type: "int", nullable: false),
                ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
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

        _ = migrationBuilder.CreateTable(
            name: "TaggingRequestContracts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RequesterUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                TagOwnerUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                TargetItemId = table.Column<int>(type: "int", nullable: false),
                RequestedTagId = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ConsumedRightAssetId = table.Column<int>(type: "int", nullable: true),
                ContractType = table.Column<string>(type: "nvarchar(21)", maxLength: 21, nullable: false),
                RequesterMessage = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                OfferedTargetItemId = table.Column<int>(type: "int", nullable: true),
                OfferedTagId = table.Column<int>(type: "int", nullable: true),
                TargetPublicTradeOfferId = table.Column<int>(type: "int", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TaggingRequestContracts", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_Items_OfferedTargetItemId",
                    column: x => x.OfferedTargetItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_Items_TargetItemId",
                    column: x => x.TargetItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_RightAssets_ConsumedRightAssetId",
                    column: x => x.ConsumedRightAssetId,
                    principalTable: "RightAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_Tags_OfferedTagId",
                    column: x => x.OfferedTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TaggingRequestContracts_Tags_RequestedTagId",
                    column: x => x.RequestedTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateTable(
            name: "TagWeightLedgers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TagId = table.Column<int>(type: "int", nullable: false),
                SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: true),
                ConsumedRightAssetId = table.Column<int>(type: "int", nullable: true),
                Delta = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagWeightLedgers", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TagWeightLedgers_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TagWeightLedgers_RightAssets_ConsumedRightAssetId",
                    column: x => x.ConsumedRightAssetId,
                    principalTable: "RightAssets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TagWeightLedgers_TagRelations_SourceId",
                    column: x => x.SourceId,
                    principalTable: "TagRelations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                _ = table.ForeignKey(
                    name: "FK_TagWeightLedgers_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_RightAssets_OwnerId",
            table: "RightAssets",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_ConsumedRightAssetId",
            table: "TaggingRequestContracts",
            column: "ConsumedRightAssetId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTagId",
            table: "TaggingRequestContracts",
            column: "OfferedTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OfferedTargetItemId",
            table: "TaggingRequestContracts",
            column: "OfferedTargetItemId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_OwnerId",
            table: "TaggingRequestContracts",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_RequestedTagId",
            table: "TaggingRequestContracts",
            column: "RequestedTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TaggingRequestContracts_TargetItemId",
            table: "TaggingRequestContracts",
            column: "TargetItemId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagRelationWeightAuditLogs_OwnerId",
            table: "TagRelationWeightAuditLogs",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightAuditLogs_OwnerId",
            table: "TagWeightAuditLogs",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightLedgers_ConsumedRightAssetId",
            table: "TagWeightLedgers",
            column: "ConsumedRightAssetId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightLedgers_OwnerId",
            table: "TagWeightLedgers",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightLedgers_SourceId",
            table: "TagWeightLedgers",
            column: "SourceId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagWeightLedgers_TagId",
            table: "TagWeightLedgers",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TaggingRequestContracts");

        _ = migrationBuilder.DropTable(
            name: "TagRelationWeightAuditLogs");

        _ = migrationBuilder.DropTable(
            name: "TagWeightAuditLogs");

        _ = migrationBuilder.DropTable(
            name: "TagWeightLedgers");

        _ = migrationBuilder.DropTable(
            name: "RightAssets");

        _ = migrationBuilder.RenameColumn(
            name: "CachedWeight",
            table: "Tags",
            newName: "Weight");
    }
}