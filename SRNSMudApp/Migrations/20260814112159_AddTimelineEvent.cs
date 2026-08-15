using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddTimelineEvent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.CreateTable(
            name: "TimelineEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TargetType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                TargetItemId = table.Column<int>(type: "int", nullable: true),
                TargetTagId = table.Column<int>(type: "int", nullable: true),
                FollowedTagId = table.Column<int>(type: "int", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                PreviousWeight = table.Column<int>(type: "int", nullable: true),
                NewWeight = table.Column<int>(type: "int", nullable: true),
                OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_TimelineEvents", x => x.Id);
                _ = table.ForeignKey(
                    name: "FK_TimelineEvents_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TimelineEvents_Items_TargetItemId",
                    column: x => x.TargetItemId,
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                _ = table.ForeignKey(
                    name: "FK_TimelineEvents_Tags_FollowedTagId",
                    column: x => x.FollowedTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                _ = table.ForeignKey(
                    name: "FK_TimelineEvents_Tags_TargetTagId",
                    column: x => x.TargetTagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_FollowedTagId",
            table: "TimelineEvents",
            column: "FollowedTagId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_OwnerId",
            table: "TimelineEvents",
            column: "OwnerId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetItemId",
            table: "TimelineEvents",
            column: "TargetItemId");

        _ = migrationBuilder.CreateIndex(
            name: "IX_TimelineEvents_TargetTagId",
            table: "TimelineEvents",
            column: "TargetTagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropTable(
            name: "TimelineEvents");
    }
}