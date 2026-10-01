using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Mhop
{
    /// <inheritdoc />
    public partial class MhopBottleMessageAiReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiReviewNote",
                table: "mhop_bottle_messages",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReviewedAt",
                table: "mhop_bottle_messages",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiReviewNote",
                table: "mhop_bottle_messages");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "mhop_bottle_messages");
        }
    }
}
