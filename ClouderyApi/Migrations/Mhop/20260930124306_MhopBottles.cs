using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace ClouderyApi.Migrations.Mhop
{
    /// <inheritdoc />
    public partial class MhopBottles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mhop_bottles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Crisis = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AiFlag = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: ""),
                    ReviewNote = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, defaultValue: ""),
                    PickerUserId = table.Column<int>(type: "int", nullable: true),
                    PickedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedByUserId = table.Column<int>(type: "int", nullable: true),
                    EndReason = table.Column<int>(type: "int", nullable: true),
                    ReportedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReportedBy = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    ReportReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    ThrowerLastReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PickerLastReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_bottles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mhop_bottle_messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BottleId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Crisis = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AiFlag = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mhop_bottle_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mhop_bottle_messages_mhop_bottles_BottleId",
                        column: x => x.BottleId,
                        principalTable: "mhop_bottles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottle_messages_BottleId_Id",
                table: "mhop_bottle_messages",
                columns: new[] { "BottleId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottle_messages_CreatedAt",
                table: "mhop_bottle_messages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottle_messages_SenderUserId",
                table: "mhop_bottle_messages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottles_LastMessageAt",
                table: "mhop_bottles",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottles_PickerUserId",
                table: "mhop_bottles",
                column: "PickerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottles_Status_CreatedAt",
                table: "mhop_bottles",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_mhop_bottles_UserId",
                table: "mhop_bottles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mhop_bottle_messages");

            migrationBuilder.DropTable(
                name: "mhop_bottles");
        }
    }
}
