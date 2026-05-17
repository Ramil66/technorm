using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechNormBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceEventCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceEventCount",
                table: "tech_routes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceEventCount",
                table: "tech_routes");
        }
    }
}
