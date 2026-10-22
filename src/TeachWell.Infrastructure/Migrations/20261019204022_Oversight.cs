using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Oversight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "SupportTickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CourseGuideRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StandardsName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StandardsVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    CheckedItems = table.Column<int>(type: "integer", nullable: false),
                    OpenSteps = table.Column<int>(type: "integer", nullable: false),
                    RequiredOpenSteps = table.Column<int>(type: "integer", nullable: false),
                    OpenPlaces = table.Column<int>(type: "integer", nullable: false),
                    DonePlaces = table.Column<int>(type: "integer", nullable: false),
                    NotApplicablePlaces = table.Column<int>(type: "integer", nullable: false),
                    OpenByAspectJson = table.Column<string>(type: "text", nullable: false),
                    OpenByCheckJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseGuideRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseGuideRecords_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseGuideRecords_CourseId_Day",
                table: "CourseGuideRecords",
                columns: new[] { "CourseId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseGuideRecords_TenantId_Day",
                table: "CourseGuideRecords",
                columns: new[] { "TenantId", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseGuideRecords");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SupportTickets");
        }
    }
}
