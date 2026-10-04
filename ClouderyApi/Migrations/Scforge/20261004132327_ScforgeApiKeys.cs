using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgeApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scforge_api_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Prefix = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Scopes = table.Column<string>(type: "longtext", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    CreatedByCasdoorId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastUsedIp = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_api_keys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_api_keys_KeyHash",
                table: "scforge_api_keys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scforge_api_keys_RevokedAt",
                table: "scforge_api_keys",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_api_keys_UserId_CreatedAt",
                table: "scforge_api_keys",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scforge_api_keys");
        }
    }
}
