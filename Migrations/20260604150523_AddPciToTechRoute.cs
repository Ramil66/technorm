using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechNormBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddPciToTechRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SourceEventCount",
                table: "tech_routes",
                newName: "source_event_count");

            migrationBuilder.RenameColumn(
                name: "IsAutoUpdate",
                table: "tech_routes",
                newName: "is_auto_update");

            migrationBuilder.AddColumn<decimal>(
                name: "last_pci",
                table: "tech_routes",
                type: "numeric(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_pci_calculated_at",
                table: "tech_routes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_pci_status",
                table: "tech_routes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_pci_summary",
                table: "tech_routes",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_pci",
                table: "tech_routes");

            migrationBuilder.DropColumn(
                name: "last_pci_calculated_at",
                table: "tech_routes");

            migrationBuilder.DropColumn(
                name: "last_pci_status",
                table: "tech_routes");

            migrationBuilder.DropColumn(
                name: "last_pci_summary",
                table: "tech_routes");

            migrationBuilder.RenameColumn(
                name: "source_event_count",
                table: "tech_routes",
                newName: "SourceEventCount");

            migrationBuilder.RenameColumn(
                name: "is_auto_update",
                table: "tech_routes",
                newName: "IsAutoUpdate");
        }
    }
}
