using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RuItUnion.FeedbackBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "FeedbackBot");

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "FeedbackBot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    MentionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.UniqueConstraint("AK_Roles_Name", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "FeedbackBot",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    UserName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FirstName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bans",
                schema: "FeedbackBot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bans_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "FeedbackBot",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleMembers",
                schema: "FeedbackBot",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMembers", x => new { x.RoleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RoleMembers_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "FeedbackBot",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleMembers_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "FeedbackBot",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                schema: "FeedbackBot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThreadId = table.Column<int>(type: "integer", nullable: false),
                    UserChatId = table.Column<long>(type: "bigint", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                    table.UniqueConstraint("AK_Topics_ThreadId", x => x.ThreadId);
                    table.ForeignKey(
                        name: "FK_Topics_Users_UserChatId",
                        column: x => x.UserChatId,
                        principalSchema: "FeedbackBot",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Replies",
                schema: "FeedbackBot",
                columns: table => new
                {
                    ChatMessageId = table.Column<int>(type: "integer", nullable: false),
                    ChatThreadId = table.Column<int>(type: "integer", nullable: false),
                    UserMessageId = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Replies", x => x.ChatMessageId);
                    table.ForeignKey(
                        name: "FK_Replies_Topics_ChatThreadId",
                        column: x => x.ChatThreadId,
                        principalSchema: "FeedbackBot",
                        principalTable: "Topics",
                        principalColumn: "ThreadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "FeedbackBot",
                table: "Roles",
                columns: new[] { "Id", "MentionEnabled", "Name" },
                values: new object[,]
                {
                    { -2, false, "ban_list" },
                    { -1, false, "admin" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bans_UserId_Until",
                schema: "FeedbackBot",
                table: "Bans",
                columns: new[] { "UserId", "Until" });

            migrationBuilder.CreateIndex(
                name: "IX_Replies_ChatThreadId",
                schema: "FeedbackBot",
                table: "Replies",
                column: "ChatThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMembers_RoleId",
                schema: "FeedbackBot",
                table: "RoleMembers",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleMembers_UserId",
                schema: "FeedbackBot",
                table: "RoleMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_ThreadId",
                schema: "FeedbackBot",
                table: "Topics",
                column: "ThreadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_UserChatId",
                schema: "FeedbackBot",
                table: "Topics",
                column: "UserChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                schema: "FeedbackBot",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bans",
                schema: "FeedbackBot");

            migrationBuilder.DropTable(
                name: "Replies",
                schema: "FeedbackBot");

            migrationBuilder.DropTable(
                name: "RoleMembers",
                schema: "FeedbackBot");

            migrationBuilder.DropTable(
                name: "Topics",
                schema: "FeedbackBot");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "FeedbackBot");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "FeedbackBot");
        }
    }
}
