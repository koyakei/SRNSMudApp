using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class RemoveTradeModels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TagRelationRights");

        _ = migrationBuilder.DropTable(
            name: "TradeTransactions");

        _ = migrationBuilder.DropTable(
            name: "RightTradeDemands");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "RightTradeDemands",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                OfferStatus = table.Column<int>(type: "int", nullable: false),
                OfferedWeight = table.Column<int>(type: "int", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_RightTradeDemands", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_RightTradeDemands_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_RightTradeDemands_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
            name: "TagRelationRights",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                TagId = table.Column<int>(type: "int", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Weight = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TagRelationRights", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TagRelationRights_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TagRelationRights_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        _ = migrationBuilder.CreateTable(
            name: "TradeTransactions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BuyerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                DemandId = table.Column<int>(type: "int", nullable: false),
                CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                TradedWeight = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TradeTransactions", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TradeTransactions_AspNetUsers_BuyerId",
                    column: x => x.BuyerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TradeTransactions_RightTradeDemands_DemandId",
                    column: x => x.DemandId,
                    principalTable: "RightTradeDemands",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_RightTradeDemands_OwnerId",
            table: "RightTradeDemands",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_RightTradeDemands_TagId",
            table: "RightTradeDemands",
            column: "TagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagRelationRights_OwnerId",
            table: "TagRelationRights",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TagRelationRights_TagId",
            table: "TagRelationRights",
            column: "TagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TradeTransactions_BuyerId",
            table: "TradeTransactions",
            column: "BuyerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TradeTransactions_DemandId",
            table: "TradeTransactions",
            column: "DemandId");
    }
}