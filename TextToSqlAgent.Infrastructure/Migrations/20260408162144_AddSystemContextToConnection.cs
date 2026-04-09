using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextToSqlAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemContextToConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessContext",
                table: "Connections",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NamingConventionNotes",
                table: "Connections",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemDomain",
                table: "Connections",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessContext",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "NamingConventionNotes",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "SystemDomain",
                table: "Connections");
        }
    }
}
