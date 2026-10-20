using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flower.Agents.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "agents");

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                schema: "agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Question = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Intent = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Verdict = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    AnswerText = table.Column<string>(type: "text", nullable: true),
                    CitedSourcesJson = table.Column<string>(type: "text", nullable: false),
                    StepsJson = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Feedback = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FeedbackNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FeedbackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeEntries",
                schema: "agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CanonicalQuestion = table.Column<string>(type: "text", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<string>(type: "text", nullable: false),
                    Aliases = table.Column<string>(type: "text", nullable: false),
                    SourceRefs = table.Column<string>(type: "text", nullable: false),
                    SourceSpaces = table.Column<string>(type: "text", nullable: true),
                    Embedding = table.Column<byte[]>(type: "bytea", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ConfirmationCount = table.Column<int>(type: "integer", nullable: false),
                    DisputeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ConfirmedByEmails = table.Column<string>(type: "text", nullable: false),
                    CreatedFromRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededById = table.Column<Guid>(type: "uuid", nullable: true),
                    LastDisputeNote = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_Feedback",
                schema: "agents",
                table: "AgentRuns",
                column: "Feedback");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_RequestId",
                schema: "agents",
                table: "AgentRuns",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_UserEmail_CreatedAt",
                schema: "agents",
                table: "AgentRuns",
                columns: new[] { "UserEmail", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEntries_Status",
                schema: "agents",
                table: "KnowledgeEntries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRuns",
                schema: "agents");

            migrationBuilder.DropTable(
                name: "KnowledgeEntries",
                schema: "agents");
        }
    }
}
