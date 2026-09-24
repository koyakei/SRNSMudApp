using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class _20260924031249_AddUserDepositWalletsAndJpycTransactions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JpycDepositTransactions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                DepositAddress = table.Column<string>(type: "nvarchar(450)", nullable: false),
                TransactionHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                NetworkName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AmountJpyc = table.Column<int>(type: "int", nullable: false),
                TargetTagId = table.Column<int>(type: "int", nullable: false),
                RightAssetAmount = table.Column<int>(type: "int", nullable: false),
                RightAssetId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JpycDepositTransactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_JpycDepositTransactions_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserDepositWallets",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                NetworkName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                DepositAddress = table.Column<string>(type: "nvarchar(450)", nullable: false),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserDepositWallets", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserDepositWallets_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JpycDepositTransactions_DepositAddress",
            table: "JpycDepositTransactions",
            column: "DepositAddress");

        migrationBuilder.CreateIndex(
            name: "IX_JpycDepositTransactions_OwnerId",
            table: "JpycDepositTransactions",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_JpycDepositTransactions_TransactionHash",
            table: "JpycDepositTransactions",
            column: "TransactionHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserDepositWallets_DepositAddress",
            table: "UserDepositWallets",
            column: "DepositAddress");

        migrationBuilder.CreateIndex(
            name: "IX_UserDepositWallets_OwnerId_NetworkName",
            table: "UserDepositWallets",
            columns: new[] { "OwnerId", "NetworkName" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "JpycDepositTransactions");

        migrationBuilder.DropTable(
            name: "UserDepositWallets");
    }
}
