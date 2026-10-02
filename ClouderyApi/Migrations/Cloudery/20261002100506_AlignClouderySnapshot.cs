using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClouderyApi.Migrations.Cloudery
{
    /// <summary>
    /// 仅用于对齐模型快照：拆分后 ClouderyContext 不再包含 Zhuxs* 三张表，
    /// 它们已归属 ZhuxsContext（见 Migrations/Zhuxs/20261002100200_InitialZhuxs）。
    /// 故 Up/Down 均为空操作——绝不能真的 DROP 这三张表。
    /// </summary>
    public partial class AlignClouderySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 空操作：Zhuxs* 表改由 ZhuxsContext 管理，这里只更新模型快照
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 空操作：同上
        }
    }
}
