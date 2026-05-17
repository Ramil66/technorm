using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechNormBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAutoUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAutoUpdate",
                table: "tech_routes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAutoUpdate",
                table: "tech_routes");
        }
    }
}
