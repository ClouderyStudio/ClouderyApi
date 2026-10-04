using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Scforge
{
    /// <inheritdoc />
    public partial class ScforgeInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scforge_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PluginId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ParentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Body = table.Column<string>(type: "longtext", nullable: false),
                    AuthorId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    AuthorName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    AuthorAvatar = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Upvotes = table.Column<int>(type: "int", nullable: false),
                    Downvotes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_comments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scforge_plugins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Summary = table.Column<string>(type: "varchar(280)", maxLength: 280, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: false),
                    Readme = table.Column<string>(type: "longtext", nullable: false),
                    Category = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Tags = table.Column<string>(type: "longtext", nullable: false),
                    TagsText = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    GameVersionsText = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    GameVersion = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    IconUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    Gallery = table.Column<string>(type: "longtext", nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    IssuesUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    License = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    LicenseUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    DonationUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    DiscordUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    AuthorId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    AuthorName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    AuthorAvatar = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    Downloads = table.Column<int>(type: "int", nullable: false),
                    Upvotes = table.Column<int>(type: "int", nullable: false),
                    Downvotes = table.Column<int>(type: "int", nullable: false),
                    CommentCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Featured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_plugins", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scforge_votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_votes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scforge_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PluginId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Changelog = table.Column<string>(type: "longtext", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    GameVersion = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    GameVersions = table.Column<string>(type: "longtext", nullable: false),
                    Dependencies = table.Column<string>(type: "longtext", nullable: false),
                    Downloads = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scforge_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scforge_versions_scforge_plugins_PluginId",
                        column: x => x.PluginId,
                        principalTable: "scforge_plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_comments_AuthorId_CreatedAt",
                table: "scforge_comments",
                columns: new[] { "AuthorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_comments_ParentId",
                table: "scforge_comments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_comments_PluginId_CreatedAt",
                table: "scforge_comments",
                columns: new[] { "PluginId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_AuthorId_UpdatedAt",
                table: "scforge_plugins",
                columns: new[] { "AuthorId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_Slug",
                table: "scforge_plugins",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_Status_Category",
                table: "scforge_plugins",
                columns: new[] { "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_Status_Downloads",
                table: "scforge_plugins",
                columns: new[] { "Status", "Downloads" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_plugins_Status_UpdatedAt",
                table: "scforge_plugins",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_versions_PluginId_PublishedAt",
                table: "scforge_versions",
                columns: new[] { "PluginId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_versions_Sha256",
                table: "scforge_versions",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_scforge_votes_TargetType_TargetId",
                table: "scforge_votes",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_scforge_votes_UserId_TargetType_TargetId",
                table: "scforge_votes",
                columns: new[] { "UserId", "TargetType", "TargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scforge_comments");

            migrationBuilder.DropTable(
                name: "scforge_versions");

            migrationBuilder.DropTable(
                name: "scforge_votes");

            migrationBuilder.DropTable(
                name: "scforge_plugins");
        }
    }
}
