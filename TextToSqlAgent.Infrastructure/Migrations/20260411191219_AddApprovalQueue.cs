using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TextToSqlAgent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalQueues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConnectionId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SqlStatement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EstimatedRows = table.Column<int>(type: "int", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeoutAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseAction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ModifiedSql = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExecutionResult = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    AffectedRows = table.Column<int>(type: "int", nullable: true),
                    Warnings = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    HasWhereClause = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalQueues_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalQueues_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueue_Status",
                table: "ApprovalQueues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueue_UserId",
                table: "ApprovalQueues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueues_ConnectionId",
                table: "ApprovalQueues",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueues_CreatedAt",
                table: "ApprovalQueues",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalQueues_TimeoutAt",
                table: "ApprovalQueues",
                column: "TimeoutAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalQueues");
        }
    }
}
