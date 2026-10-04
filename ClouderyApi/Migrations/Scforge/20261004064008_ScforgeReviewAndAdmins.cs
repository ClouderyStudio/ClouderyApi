using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgeReviewAndAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "scforge_versions",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "scforge_versions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "scforge_versions",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "scforge_versions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "scforge_plugins",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "scforge_plugins",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "scforge_plugins",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scforge_admins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Avatar = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    Role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Permissions = table.Column<string>(type: "longtext", nullable: false),
                    GrantedBy = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    FromConfig = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_admins", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_admins_Role",
                table: "scforge_admins",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_admins_UserId",
                table: "scforge_admins",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scforge_admins");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "scforge_versions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "scforge_versions");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "scforge_versions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "scforge_versions");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "scforge_plugins");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "scforge_plugins");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "scforge_plugins");
        }
    }
}
