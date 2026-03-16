using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextToSqlAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEnhancedMessageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CorrectionAttempts",
                table: "Messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionHistory",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingSteps",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueryExplanation",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Success",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedQueries",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasCorrected",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectionAttempts",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "CorrectionHistory",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ProcessingSteps",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "QueryExplanation",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Success",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SuggestedQueries",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "WasCorrected",
                table: "Messages");
        }
    }
}
