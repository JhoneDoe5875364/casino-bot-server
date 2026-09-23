using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PragmaticBot.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Providers",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 1);   // 1 = PragmaticPlay: what every existing account already had

            // Existing rows were written before the column existed, so EF's default does not reach
            // them. 본사 gets both providers — otherwise nobody could ever grant Evolution downward,
            // since a branch can only pass on what it holds.
            migrationBuilder.Sql("UPDATE Users SET Providers = 1 WHERE Providers = 0;");
            migrationBuilder.Sql("UPDATE Users SET Providers = 3 WHERE Role = 30;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Providers",
                table: "Users");
        }
    }
}
