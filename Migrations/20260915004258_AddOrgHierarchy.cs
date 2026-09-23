using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PragmaticBot.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TreePath",
                table: "Users",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Backfill from the old IsAdmin flag before it goes away:
            // the existing admin becomes the root 본사, everyone else a 회원 beneath it.
            migrationBuilder.Sql(@"
                UPDATE Users SET Role = 30, TreePath = '/', ParentId = NULL WHERE IsAdmin = 1;
                SET @owner = (SELECT Id FROM (SELECT Id FROM Users WHERE Role = 30 ORDER BY Id LIMIT 1) AS o);
                UPDATE Users
                   SET Role = 0,
                       ParentId = @owner,
                       TreePath = CONCAT('/', @owner, '/')
                 WHERE IsAdmin = 0 AND @owner IS NOT NULL;
                UPDATE Users SET TreePath = '/' WHERE TreePath = '' OR TreePath IS NULL;
            ");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ParentId_Role",
                table: "Users",
                columns: new[] { "ParentId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TreePath",
                table: "Users",
                column: "TreePath");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_ParentId",
                table: "Users",
                column: "ParentId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_ParentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ParentId_Role",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TreePath",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TreePath",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
