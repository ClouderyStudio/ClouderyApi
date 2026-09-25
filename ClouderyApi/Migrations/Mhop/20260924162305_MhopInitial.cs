using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ClouderyApi.Migrations.Mhop
{
    /// <inheritdoc />
    public partial class MhopInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_ai_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Module = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ReplyId = table.Column<int>(type: "int", nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    Engine = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_ai_logs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_assessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AssessmentType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    InputData = table.Column<string>(type: "text", nullable: false),
                    AiResult = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_assessments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_likes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_likes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_posts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IsAnonymous = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Board = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "mood"),
                    Images = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Crisis = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ReviewNote = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_posts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    CasdoorId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "user"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    Avatar = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    Badge = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_replies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IsAnonymous = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Images = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsAi = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    Crisis = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    ReviewNote = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    Recalled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    RecallReason = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_replies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mhop_replies_mhop_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "mhop_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_ai_logs_ReplyId",
                table: "mhop_ai_logs",
                column: "ReplyId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_ai_logs_SessionId",
                table: "mhop_ai_logs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_ai_logs_UserId",
                table: "mhop_ai_logs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_assessments_UserId",
                table: "mhop_assessments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_likes_TargetType_TargetId",
                table: "mhop_likes",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_mhop_likes_UserId",
                table: "mhop_likes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_likes_UserId_TargetType_TargetId",
                table: "mhop_likes",
                columns: new[] { "UserId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mhop_posts_Board",
                table: "mhop_posts",
                column: "Board");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_posts_CreatedAt",
                table: "mhop_posts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_posts_Status",
                table: "mhop_posts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_posts_UserId",
                table: "mhop_posts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_replies_PostId",
                table: "mhop_replies",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_replies_Recalled",
                table: "mhop_replies",
                column: "Recalled");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_replies_Status",
                table: "mhop_replies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_users_CasdoorId",
                table: "mhop_users",
                column: "CasdoorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mhop_users_Email",
                table: "mhop_users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mhop_users_Phone",
                table: "mhop_users",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mhop_users_Username",
                table: "mhop_users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mhop_ai_logs");

            migrationBuilder.DropTable(
                name: "mhop_assessments");

            migrationBuilder.DropTable(
                name: "mhop_likes");

            migrationBuilder.DropTable(
                name: "mhop_replies");

            migrationBuilder.DropTable(
                name: "mhop_users");

            migrationBuilder.DropTable(
                name: "mhop_posts");
        }
    }
}
