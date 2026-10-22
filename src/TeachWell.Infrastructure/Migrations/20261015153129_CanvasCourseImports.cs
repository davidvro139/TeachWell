using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CanvasCourseImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Elements_CourseId_SourceElementId",
                table: "Elements");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Elements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceStatus",
                table: "Elements",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Elements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceWarning",
                table: "Elements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetElementId",
                table: "Elements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CanvasConnectionId",
                table: "Courses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportWarningsJson",
                table: "Courses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "Courses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Revision",
                table: "Courses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SourceOrigin",
                table: "Courses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElementSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElementId = table.Column<Guid>(type: "uuid", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    PlainText = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    SourceStatus = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElementSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElementSnapshots_Elements_ElementId",
                        column: x => x.ElementId,
                        principalTable: "Elements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Elements_CourseId_Type_SourceElementId",
                table: "Elements",
                columns: new[] { "CourseId", "Type", "SourceElementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Elements_TargetElementId",
                table: "Elements",
                column: "TargetElementId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_CanvasConnectionId",
                table: "Courses",
                column: "CanvasConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_TenantId_OwnerId_SourceOrigin_SourceCourseId",
                table: "Courses",
                columns: new[] { "TenantId", "OwnerId", "SourceOrigin", "SourceCourseId" },
                unique: true,
                filter: "\"SourceOrigin\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ElementSnapshots_ElementId_FetchedAt",
                table: "ElementSnapshots",
                columns: new[] { "ElementId", "FetchedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_CanvasConnections_CanvasConnectionId",
                table: "Courses",
                column: "CanvasConnectionId",
                principalTable: "CanvasConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Elements_Elements_TargetElementId",
                table: "Elements",
                column: "TargetElementId",
                principalTable: "Elements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("CREATE FUNCTION teachwell_guard_snapshot_mutation() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'Content snapshots are append-only'; END; $$ LANGUAGE plpgsql; CREATE TRIGGER immutable_element_snapshots BEFORE UPDATE OR DELETE ON \"ElementSnapshots\" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_snapshot_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER immutable_element_snapshots ON \"ElementSnapshots\"; DROP FUNCTION teachwell_guard_snapshot_mutation();");
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_CanvasConnections_CanvasConnectionId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Elements_Elements_TargetElementId",
                table: "Elements");

            migrationBuilder.DropTable(
                name: "ElementSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Elements_CourseId_Type_SourceElementId",
                table: "Elements");

            migrationBuilder.DropIndex(
                name: "IX_Elements_TargetElementId",
                table: "Elements");

            migrationBuilder.DropIndex(
                name: "IX_Courses_CanvasConnectionId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Courses_TenantId_OwnerId_SourceOrigin_SourceCourseId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "SourceStatus",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "SourceWarning",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "TargetElementId",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "CanvasConnectionId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ImportWarningsJson",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "SourceOrigin",
                table: "Courses");

            migrationBuilder.CreateIndex(
                name: "IX_Elements_CourseId_SourceElementId",
                table: "Elements",
                columns: new[] { "CourseId", "SourceElementId" },
                unique: true);
        }
    }
}
