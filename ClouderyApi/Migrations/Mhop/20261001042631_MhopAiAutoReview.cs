using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Mhop
{
    /// <inheritdoc />
    public partial class MhopAiAutoReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiFlag",
                table: "mhop_replies",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiReviewNote",
                table: "mhop_replies",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReviewedAt",
                table: "mhop_replies",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiFlag",
                table: "mhop_posts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiReviewNote",
                table: "mhop_posts",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReviewedAt",
                table: "mhop_posts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiReviewNote",
                table: "mhop_bottles",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReviewedAt",
                table: "mhop_bottles",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiFlag",
                table: "mhop_replies");

            migrationBuilder.DropColumn(
                name: "AiReviewNote",
                table: "mhop_replies");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "mhop_replies");

            migrationBuilder.DropColumn(
                name: "AiFlag",
                table: "mhop_posts");

            migrationBuilder.DropColumn(
                name: "AiReviewNote",
                table: "mhop_posts");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "mhop_posts");

            migrationBuilder.DropColumn(
                name: "AiReviewNote",
                table: "mhop_bottles");

            migrationBuilder.DropColumn(
                name: "AiReviewedAt",
                table: "mhop_bottles");
        }
    }
}
