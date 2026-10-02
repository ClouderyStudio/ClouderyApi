using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Zhuxs
{
    /// <summary>
    /// ZhuxsContext 的 baseline 迁移：ZhuxsWhitelists / ZhuxsTerms / ZhuxsApplications
    /// 三张表早于 EF 迁移存在（原先由共享的 ClouderyApiContext 通过 GenerateCreateScript 建表），
    /// 因此这里只做幂等建表（IF NOT EXISTS），对既有生产库等价于「标记已应用」；
    /// Down 故意留空，避免回滚误删已有数据。
    /// </summary>
    public partial class InitialZhuxs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS `ZhuxsApplications` (
    `Id` varchar(255) NOT NULL,
    `Passed` tinyint(1) NOT NULL,
    `SubmissionDate` datetime(6) NOT NULL,
    `Sharables` json NULL,
    CONSTRAINT `PK_ZhuxsApplications` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ZhuxsTerms` (
    `Id` varchar(255) NOT NULL,
    `RecordDate` longtext NOT NULL,
    `Description` longtext NOT NULL,
    `Information` json NOT NULL,
    `Files` json NULL,
    CONSTRAINT `PK_ZhuxsTerms` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ZhuxsWhitelists` (
    `Id` varchar(255) NOT NULL,
    `Code` longtext NOT NULL,
    CONSTRAINT `PK_ZhuxsWhitelists` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // baseline 迁移：不回滚（表可能早于本迁移存在，Drop 会误删数据）
        }
    }
}
