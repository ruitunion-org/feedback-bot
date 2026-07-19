using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuItUnion.FeedbackBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Spam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpamMessages",
                schema: "FeedbackBot",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Update = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpamMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpamMessages_Users_Id",
                        column: x => x.Id,
                        principalSchema: "FeedbackBot",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpamMessages",
                schema: "FeedbackBot");
        }
    }
}
