using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgePrivacyAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessHint",
                table: "scforge_plugins",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessMode",
                table: "scforge_plugins",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                // 存量资源必须是 public，否则 AccessMode = "" 会让它们从公开目录里整体消失
                // （公开目录按 AccessMode == 'public' 过滤）。EF 生成的空串默认值在这里是错的。
                defaultValue: "public");

            migrationBuilder.AddColumn<string>(
                name: "AccessPasswordHash",
                table: "scforge_plugins",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scforge_access_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PluginId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_access_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scforge_access_grants_scforge_plugins_PluginId",
                        column: x => x.PluginId,
                        principalTable: "scforge_plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_access_grants_PluginId_CreatedAt",
                table: "scforge_access_grants",
                columns: new[] { "PluginId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_access_grants_PluginId_UserId",
                table: "scforge_access_grants",
                columns: new[] { "PluginId", "UserId" },
                unique: true);

            // 兜底：把任何非受控取值归一成 public。列上的 defaultValue 只保证新插入的行，
            // 这一条保证存量行（以及将来手工改坏的数据）不会静默变成「谁也访问不了」。
            migrationBuilder.Sql("""
                UPDATE scforge_plugins SET AccessMode = 'public'
                WHERE AccessMode IS NULL OR AccessMode NOT IN ('public', 'password', 'whitelist')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scforge_access_grants");

            migrationBuilder.DropColumn(
                name: "AccessHint",
                table: "scforge_plugins");

            migrationBuilder.DropColumn(
                name: "AccessMode",
                table: "scforge_plugins");

            migrationBuilder.DropColumn(
                name: "AccessPasswordHash",
                table: "scforge_plugins");
        }
    }
}
