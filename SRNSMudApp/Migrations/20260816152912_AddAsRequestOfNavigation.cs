using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations
{
    /// <inheritdoc />
    public partial class AddAsRequestOfNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaggingRequestContracts_RequestItemId",
                table: "TaggingRequestContracts");

            migrationBuilder.CreateIndex(
                name: "IX_TaggingRequestContracts_RequestItemId",
                table: "TaggingRequestContracts",
                column: "RequestItemId",
                unique: true,
                filter: "[RequestItemId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaggingRequestContracts_RequestItemId",
                table: "TaggingRequestContracts");

            migrationBuilder.CreateIndex(
                name: "IX_TaggingRequestContracts_RequestItemId",
                table: "TaggingRequestContracts",
                column: "RequestItemId");
        }
    }
}