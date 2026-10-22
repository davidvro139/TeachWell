using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Readiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadinessRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StandardSetVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StandardsName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StandardsVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ContentAsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnapshotIdsJson = table.Column<string>(type: "text", nullable: false),
                    GuideSummaryJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TakenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WithdrawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadinessRequests_AspNetUsers_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadinessRequests_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadinessRequests_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadinessRequests_StandardSetVersions_StandardSetVersionId",
                        column: x => x.StandardSetVersionId,
                        principalTable: "StandardSetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Endorsements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardSetVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StandardsName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StandardsVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ContentAsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnapshotIdsJson = table.Column<string>(type: "text", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndorsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Endorsements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Endorsements_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Endorsements_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Endorsements_ReadinessRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ReadinessRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Endorsements_StandardSetVersions_StandardSetVersionId",
                        column: x => x.StandardSetVersionId,
                        principalTable: "StandardSetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_CourseId",
                table: "Endorsements",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_RequestId",
                table: "Endorsements",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_ReviewerId",
                table: "Endorsements",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_Endorsements_StandardSetVersionId",
                table: "Endorsements",
                column: "StandardSetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessRequests_CourseId",
                table: "ReadinessRequests",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessRequests_RequestedBy",
                table: "ReadinessRequests",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessRequests_ReviewerId",
                table: "ReadinessRequests",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessRequests_StandardSetVersionId",
                table: "ReadinessRequests",
                column: "StandardSetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessRequests_TenantId_Status",
                table: "ReadinessRequests",
                columns: new[] { "TenantId", "Status" });

            // ADR 013: endorsements never change, and a decided request is final. Mirrors AppDbContext.GuardImmutableRecords for direct SQL.
            migrationBuilder.Sql("""
                CREATE FUNCTION teachwell_guard_endorsement() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'Endorsements are immutable'; END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_endorsements BEFORE UPDATE OR DELETE ON "Endorsements" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_endorsement();
                CREATE FUNCTION teachwell_guard_decided_readiness() RETURNS trigger AS $$
                BEGIN
                    IF OLD."Status" IN ('Endorsed', 'ChangesRequested') THEN RAISE EXCEPTION 'Decided readiness requests are immutable'; END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_decided_readiness BEFORE UPDATE OR DELETE ON "ReadinessRequests" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_decided_readiness();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_endorsements ON "Endorsements"; DROP FUNCTION teachwell_guard_endorsement();
                DROP TRIGGER immutable_decided_readiness ON "ReadinessRequests"; DROP FUNCTION teachwell_guard_decided_readiness();
                """);
            migrationBuilder.DropTable(
                name: "Endorsements");

            migrationBuilder.DropTable(
                name: "ReadinessRequests");
        }
    }
}
