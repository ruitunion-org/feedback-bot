using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuItUnion.FeedbackBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServiceAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "FeedbackBot",
                table: "Roles",
                columns: new[] { "Id", "MentionEnabled", "Name" },
                values: new object[] { -50, false, "service_admin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "FeedbackBot",
                table: "Roles",
                keyColumn: "Id",
                keyValue: -50);
        }
    }
}
