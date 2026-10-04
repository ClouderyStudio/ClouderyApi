using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgeResourceKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "scforge_plugins",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "plugin");

            // 历史数据回填：用 .netmod 发布过的资源其实是模组（模组会下发到客户端）。
            migrationBuilder.Sql("""
                UPDATE scforge_plugins SET Kind = 'mod'
                WHERE EXISTS (
                    SELECT 1 FROM scforge_versions v
                    WHERE v.PluginId = scforge_plugins.Id AND v.FileName LIKE '%.netmod'
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_Kind_Status_Downloads",
                table: "scforge_plugins",
                columns: new[] { "Kind", "Status", "Downloads" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scforge_plugins_Kind_Status_Downloads",
                table: "scforge_plugins");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "scforge_plugins");
        }
    }
}
