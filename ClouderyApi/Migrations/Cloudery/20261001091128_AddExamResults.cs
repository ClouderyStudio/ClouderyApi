using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Cloudery
{
    /// <inheritdoc />
    public partial class AddExamResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TestId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TestTitle = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    ClientKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    SavedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Payload = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamResults", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_UserId_ClientKey",
                table: "ExamResults",
                columns: new[] { "UserId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_UserId_SavedAt",
                table: "ExamResults",
                columns: new[] { "UserId", "SavedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamResults");
        }
    }
}
