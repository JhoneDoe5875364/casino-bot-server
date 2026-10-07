using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PragmaticBot.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLogUploadEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LogUploadEnabled",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);  // 기존 계정은 지금처럼 로그 저장 켜짐으로 시작
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogUploadEnabled",
                table: "Users");
        }
    }
}
