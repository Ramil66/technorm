using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TechNormBlazor.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationNameMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_name_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    operation_id = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    is_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    confirmed_by = table.Column<int>(type: "integer", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_name_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_name_mappings_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operation_name_mappings_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_operation_name_mappings_confirmed_by",
                table: "operation_name_mappings",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "IX_operation_name_mappings_operation_id",
                table: "operation_name_mappings",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_operation_name_mappings_raw_name",
                table: "operation_name_mappings",
                column: "raw_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_name_mappings");
        }
    }
}
