using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FarmIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationalAdvisoryV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectedIntent",
                table: "InboundMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IgnoredReason",
                table: "InboundMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnalysisSource",
                table: "CropAdvisories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsCloserPhoto",
                table: "CropAdvisories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsLocation",
                table: "CropAdvisories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShortReasoningSummary",
                table: "CropAdvisories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssistantState",
                table: "Conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssistantStateJson",
                table: "Conversations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBotPromptUtc",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastDetectedIntent",
                table: "Conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWeatherPromptUtc",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationRequestedUtc",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedIntent",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "IgnoredReason",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "AnalysisSource",
                table: "CropAdvisories");

            migrationBuilder.DropColumn(
                name: "NeedsCloserPhoto",
                table: "CropAdvisories");

            migrationBuilder.DropColumn(
                name: "NeedsLocation",
                table: "CropAdvisories");

            migrationBuilder.DropColumn(
                name: "ShortReasoningSummary",
                table: "CropAdvisories");

            migrationBuilder.DropColumn(
                name: "AssistantState",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AssistantStateJson",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastBotPromptUtc",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastDetectedIntent",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastWeatherPromptUtc",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LocationRequestedUtc",
                table: "Conversations");
        }
    }
}
