using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RevisionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevisionTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BlockedFromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadyForReviewSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_AspNetUsers_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_ElementSnapshots_ReadyForReviewSnapshotId",
                        column: x => x.ReadyForReviewSnapshotId,
                        principalTable: "ElementSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_ElementSnapshots_SourceSnapshotId",
                        column: x => x.SourceSnapshotId,
                        principalTable: "ElementSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTasks_Elements_ElementId",
                        column: x => x.ElementId,
                        principalTable: "Elements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevisionTaskFindings",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionTaskFindings", x => new { x.TaskId, x.FindingId });
                    table.ForeignKey(
                        name: "FK_RevisionTaskFindings_ReviewFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "ReviewFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevisionTaskFindings_RevisionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RevisionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskComments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskComments_RevisionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RevisionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskEvents_RevisionTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RevisionTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTaskFindings_FindingId",
                table: "RevisionTaskFindings",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_AssigneeId",
                table: "RevisionTasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_CourseId",
                table: "RevisionTasks",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_ElementId",
                table: "RevisionTasks",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_ReadyForReviewSnapshotId",
                table: "RevisionTasks",
                column: "ReadyForReviewSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_ReviewerId",
                table: "RevisionTasks",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_SourceSnapshotId",
                table: "RevisionTasks",
                column: "SourceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_RevisionTasks_TenantId_CourseId",
                table: "RevisionTasks",
                columns: new[] { "TenantId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_AuthorId",
                table: "TaskComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskId",
                table: "TaskComments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvents_ActorId",
                table: "TaskEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvents_TaskId_CreatedAt",
                table: "TaskEvents",
                columns: new[] { "TaskId", "CreatedAt" });

            // Database-level immutability mirrors the EF guard so direct SQL cannot rewrite a task's history.
            migrationBuilder.Sql("""
                CREATE FUNCTION teachwell_guard_task_events() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'Task history events are append-only'; END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_task_events BEFORE UPDATE OR DELETE ON "TaskEvents" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_task_events();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_task_events ON "TaskEvents";
                DROP FUNCTION teachwell_guard_task_events();
                """);

            migrationBuilder.DropTable(
                name: "RevisionTaskFindings");

            migrationBuilder.DropTable(
                name: "TaskComments");

            migrationBuilder.DropTable(
                name: "TaskEvents");

            migrationBuilder.DropTable(
                name: "RevisionTasks");
        }
    }
}
