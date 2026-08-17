using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRNSMudApp.Migrations;

/// <inheritdoc />
public partial class AddRejectToTaggingRequest : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.AddColumn<string>(
            name: "RejectComment",
            table: "TaggingRequestContracts",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        _ = migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RejectedAt",
            table: "TaggingRequestContracts",
            type: "datetimeoffset",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        _ = migrationBuilder.DropColumn(
            name: "RejectComment",
            table: "TaggingRequestContracts");

        _ = migrationBuilder.DropColumn(
            name: "RejectedAt",
            table: "TaggingRequestContracts");
    }
}