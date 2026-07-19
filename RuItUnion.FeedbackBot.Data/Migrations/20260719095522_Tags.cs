using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuItUnion.FeedbackBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                schema: "FeedbackBot",
                table: "Topics",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Header",
                schema: "FeedbackBot",
                table: "Topics",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "Tags",
                schema: "FeedbackBot",
                table: "Topics",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_Tags",
                schema: "FeedbackBot",
                table: "Topics",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Topics_Tags",
                schema: "FeedbackBot",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Created",
                schema: "FeedbackBot",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Header",
                schema: "FeedbackBot",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Tags",
                schema: "FeedbackBot",
                table: "Topics");
        }
    }
}
