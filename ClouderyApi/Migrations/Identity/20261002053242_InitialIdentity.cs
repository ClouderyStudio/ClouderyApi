using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Identity
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 历史库中的 Users 表可能已由旧迁移创建并承载真实用户数据。
            // 使用 CREATE TABLE IF NOT EXISTS：全新库正常建表，既有库则原样保留，
            // 从而不破坏既有用户 Id 与 /exam/results 的归属关系。
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS `Users` (
    `Id` char(36) NOT NULL,
    `Username` varchar(100) NOT NULL,
    `Email` varchar(200) NULL,
    `Avatar` varchar(500) NULL,
    `CasdoorId` varchar(100) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `LastLoginAt` datetime(6) NULL,
    CONSTRAINT `PK_Users` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 该表可能承载历史用户数据（由旧迁移创建），回滚时有意不删除，
            // 避免回滚 baseline 时误删既有账号。
        }
    }
}
