using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuItUnion.FeedbackBot.Data.Migrations;

/// <inheritdoc />
public partial class _20261120185149_Queue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "AssigneeId",
            schema: "FeedbackBot",
            table: "Topics",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "InterceptMessageId",
            schema: "FeedbackBot",
            table: "Topics",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<long[]>(
            name: "InterceptVotes",
            schema: "FeedbackBot",
            table: "Topics",
            type: "bigint[]",
            nullable: false,
            defaultValue: new long[0]);

        migrationBuilder.AddColumn<bool>(
            name: "IsQueued",
            schema: "FeedbackBot",
            table: "Topics",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<long>(
            name: "LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LockedFrom",
            schema: "FeedbackBot",
            table: "Topics",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.CreateTable(
            name: "Mentors",
            schema: "FeedbackBot",
            columns: table => new
            {
                NoviceId = table.Column<long>(type: "bigint", nullable: false),
                MentorId = table.Column<long>(type: "bigint", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Mentors", x => new { x.NoviceId, x.MentorId });
                table.ForeignKey(
                    name: "FK_Mentors_Users_MentorId",
                    column: x => x.MentorId,
                    principalSchema: "FeedbackBot",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Mentors_Users_NoviceId",
                    column: x => x.NoviceId,
                    principalSchema: "FeedbackBot",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Queue",
            schema: "FeedbackBot",
            columns: table => new
            {
                UserId = table.Column<long>(type: "bigint", nullable: false),
                InQueue = table.Column<bool>(type: "boolean", nullable: false),
                LastAssignmentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)),
                AssignmentPenalty = table.Column<TimeSpan>(type: "interval", nullable: false, defaultValue: new TimeSpan(0, 0, 0, 0, 0)),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Queue", x => x.UserId);
                table.ForeignKey(
                    name: "FK_Queue_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "FeedbackBot",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Topics_AssigneeId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "AssigneeId");

        migrationBuilder.CreateIndex(
            name: "IX_Topics_InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "InterceptCandidateId");

        migrationBuilder.CreateIndex(
            name: "IX_Topics_IsQueued",
            schema: "FeedbackBot",
            table: "Topics",
            column: "IsQueued")
            .Annotation("Npgsql:IndexMethod", "HASH");

        migrationBuilder.CreateIndex(
            name: "IX_Topics_LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "LastLeavedUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Mentors_IsActive",
            schema: "FeedbackBot",
            table: "Mentors",
            column: "IsActive",
            descending: new bool[0])
            .Annotation("Npgsql:IndexMethod", "HASH");

        migrationBuilder.CreateIndex(
            name: "IX_Mentors_MentorId",
            schema: "FeedbackBot",
            table: "Mentors",
            column: "MentorId");

        migrationBuilder.CreateIndex(
            name: "IX_Mentors_NoviceId",
            schema: "FeedbackBot",
            table: "Mentors",
            column: "NoviceId")
            .Annotation("Npgsql:IndexMethod", "HASH");

        migrationBuilder.CreateIndex(
            name: "IX_Queue_InQueue",
            schema: "FeedbackBot",
            table: "Queue",
            column: "InQueue",
            descending: new bool[0])
            .Annotation("Npgsql:IndexMethod", "HASH");

        migrationBuilder.CreateIndex(
            name: "IX_Queue_LastAssignmentTime",
            schema: "FeedbackBot",
            table: "Queue",
            column: "LastAssignmentTime");

        migrationBuilder.AddForeignKey(
            name: "FK_Topics_Users_AssigneeId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "AssigneeId",
            principalSchema: "FeedbackBot",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Topics_Users_InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "InterceptCandidateId",
            principalSchema: "FeedbackBot",
            principalTable: "Users",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_Topics_Users_LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics",
            column: "LastLeavedUserId",
            principalSchema: "FeedbackBot",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Topics_Users_AssigneeId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropForeignKey(
            name: "FK_Topics_Users_InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropForeignKey(
            name: "FK_Topics_Users_LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropTable(
            name: "Mentors",
            schema: "FeedbackBot");

        migrationBuilder.DropTable(
            name: "Queue",
            schema: "FeedbackBot");

        migrationBuilder.DropIndex(
            name: "IX_Topics_AssigneeId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropIndex(
            name: "IX_Topics_InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropIndex(
            name: "IX_Topics_IsQueued",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropIndex(
            name: "IX_Topics_LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "AssigneeId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "InterceptCandidateId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "InterceptMessageId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "InterceptVotes",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "IsQueued",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "LastLeavedUserId",
            schema: "FeedbackBot",
            table: "Topics");

        migrationBuilder.DropColumn(
            name: "LockedFrom",
            schema: "FeedbackBot",
            table: "Topics");
    }
}
