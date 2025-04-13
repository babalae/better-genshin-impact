using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BetterGenshinImpact.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_group",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    order_index = table.Column<int>(type: "INTEGER", nullable: false),
                    task_name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    task_params = table.Column<string>(type: "TEXT", nullable: true),
                    schedule_expression = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    schedule_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    next_run_time = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_run_time = table.Column<DateTime>(type: "TEXT", nullable: true),
                    hotkey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    hotkey_ = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_group", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_group_order_index",
                table: "task_group",
                column: "order_index",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_group");
        }
    }
}
