using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgeGameVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scforge_game_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Beta = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_game_versions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_game_versions_SortOrder",
                table: "scforge_game_versions",
                column: "SortOrder");

            // 初始的受支持版本（生存战争用日期式编号）；之后由超管在后台维护。
            migrationBuilder.Sql("""
                INSERT INTO scforge_game_versions (Id, Version, SortOrder, Beta, CreatedAt, CreatedBy) VALUES
                    ('d0000000-0000-4000-8000-000000000003', 'x26.07.01', 3, 0, UTC_TIMESTAMP(), '系统初始化'),
                    ('d0000000-0000-4000-8000-000000000002', 'x26.06.19', 2, 0, UTC_TIMESTAMP(), '系统初始化'),
                    ('d0000000-0000-4000-8000-000000000001', 'x26.05.23', 1, 0, UTC_TIMESTAMP(), '系统初始化');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_scforge_game_versions_Version",
                table: "scforge_game_versions",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scforge_game_versions");
        }
    }
}
