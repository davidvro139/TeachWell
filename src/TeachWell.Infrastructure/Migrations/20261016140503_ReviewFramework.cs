using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "ElementSnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "Elements",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RubricProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DeliveryMethod = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricProfiles_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RubricVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricVersions_RubricProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "RubricProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    RubricVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChecksRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_AspNetUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_ElementSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "ElementSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_Elements_ElementId",
                        column: x => x.ElementId,
                        principalTable: "Elements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_RubricVersions_RubricVersionId",
                        column: x => x.RubricVersionId,
                        principalTable: "RubricVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RubricCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ElementTypesJson = table.Column<string>(type: "text", nullable: false),
                    ReviewMethod = table.Column<string>(type: "text", nullable: false),
                    EvidenceGuidance = table.Column<string>(type: "text", nullable: false),
                    RatingAnchorsJson = table.Column<string>(type: "text", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    AutomatedChecksJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricCriteria_RubricVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "RubricVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CriterionEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriterionEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CriterionEvaluations_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CriterionEvaluations_RubricCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CheckCode = table.Column<string>(type: "text", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewFindings_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewFindings_RubricCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriterionEvaluations_CriterionId",
                table: "CriterionEvaluations",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_CriterionEvaluations_ReviewId_CriterionId",
                table: "CriterionEvaluations",
                columns: new[] { "ReviewId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_CriterionId",
                table: "ReviewFindings",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_ReviewId_Fingerprint",
                table: "ReviewFindings",
                columns: new[] { "ReviewId", "Fingerprint" },
                unique: true,
                filter: "\"Fingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_CourseId",
                table: "Reviews",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ElementId",
                table: "Reviews",
                column: "ElementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ReviewerId",
                table: "Reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_RubricVersionId",
                table: "Reviews",
                column: "RubricVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_SnapshotId",
                table: "Reviews",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_TenantId_CourseId_ElementId",
                table: "Reviews",
                columns: new[] { "TenantId", "CourseId", "ElementId" });

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriteria_VersionId_Code",
                table: "RubricCriteria",
                columns: new[] { "VersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RubricProfiles_CreatedBy",
                table: "RubricProfiles",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RubricProfiles_TenantId_Name",
                table: "RubricProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RubricVersions_ProfileId_VersionNumber",
                table: "RubricVersions",
                columns: new[] { "ProfileId", "VersionNumber" },
                unique: true);

            // Database-level immutability mirrors the EF guard so direct SQL cannot rewrite published assessments.
            migrationBuilder.Sql("""
                CREATE FUNCTION teachwell_guard_published_row() RETURNS trigger AS $$
                BEGIN
                    IF OLD."Status" = 'Published' THEN RAISE EXCEPTION 'Published % rows are immutable', TG_TABLE_NAME; END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_published_rubric_versions BEFORE UPDATE OR DELETE ON "RubricVersions" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_published_row();
                CREATE TRIGGER immutable_published_reviews BEFORE UPDATE OR DELETE ON "Reviews" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_published_row();

                CREATE FUNCTION teachwell_guard_rubric_criteria() RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP <> 'DELETE' AND EXISTS (SELECT 1 FROM "RubricVersions" WHERE "Id" = NEW."VersionId" AND "Status" = 'Published'))
                        OR (TG_OP <> 'INSERT' AND EXISTS (SELECT 1 FROM "RubricVersions" WHERE "Id" = OLD."VersionId" AND "Status" = 'Published')) THEN
                        RAISE EXCEPTION 'Published rubric criteria are immutable';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_published_rubric_criteria BEFORE INSERT OR UPDATE OR DELETE ON "RubricCriteria" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_rubric_criteria();

                CREATE FUNCTION teachwell_guard_review_results() RETURNS trigger AS $$
                BEGIN
                    IF (TG_OP <> 'DELETE' AND EXISTS (SELECT 1 FROM "Reviews" WHERE "Id" = NEW."ReviewId" AND "Status" = 'Published'))
                        OR (TG_OP <> 'INSERT' AND EXISTS (SELECT 1 FROM "Reviews" WHERE "Id" = OLD."ReviewId" AND "Status" = 'Published')) THEN
                        RAISE EXCEPTION 'Published review results are immutable';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER immutable_published_evaluations BEFORE INSERT OR UPDATE OR DELETE ON "CriterionEvaluations" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_review_results();
                CREATE TRIGGER immutable_published_findings BEFORE INSERT OR UPDATE OR DELETE ON "ReviewFindings" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_review_results();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_published_findings ON "ReviewFindings";
                DROP TRIGGER immutable_published_evaluations ON "CriterionEvaluations";
                DROP FUNCTION teachwell_guard_review_results();
                DROP TRIGGER immutable_published_rubric_criteria ON "RubricCriteria";
                DROP FUNCTION teachwell_guard_rubric_criteria();
                DROP TRIGGER immutable_published_reviews ON "Reviews";
                DROP TRIGGER immutable_published_rubric_versions ON "RubricVersions";
                DROP FUNCTION teachwell_guard_published_row();
                """);
            migrationBuilder.DropTable(
                name: "CriterionEvaluations");

            migrationBuilder.DropTable(
                name: "ReviewFindings");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "RubricCriteria");

            migrationBuilder.DropTable(
                name: "RubricVersions");

            migrationBuilder.DropTable(
                name: "RubricProfiles");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "ElementSnapshots");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "Elements");
        }
    }
}
