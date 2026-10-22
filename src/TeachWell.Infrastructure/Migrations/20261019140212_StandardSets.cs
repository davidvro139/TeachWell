using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StandardSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StandardSetVersionId",
                table: "Reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "Courses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Courses",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StandardSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DeliveryMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Department = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardSets_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StandardSetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ChecksJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardSetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardSetVersions_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StandardSetVersions_AspNetUsers_PublishedBy",
                        column: x => x.PublishedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StandardSetVersions_StandardSets_SetId",
                        column: x => x.SetId,
                        principalTable: "StandardSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_StandardSetVersionId",
                table: "Reviews",
                column: "StandardSetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSets_CreatedBy",
                table: "StandardSets",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSets_TenantId_ScopeKey",
                table: "StandardSets",
                columns: new[] { "TenantId", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandardSetVersions_CreatedBy",
                table: "StandardSetVersions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSetVersions_PublishedBy",
                table: "StandardSetVersions",
                column: "PublishedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StandardSetVersions_SetId_VersionNumber",
                table: "StandardSetVersions",
                columns: new[] { "SetId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_StandardSetVersions_StandardSetVersionId",
                table: "Reviews",
                column: "StandardSetVersionId",
                principalTable: "StandardSetVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Same database-level guard as published rubric versions and reviews (ReviewFramework migration).
            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_published_standard_set_versions BEFORE UPDATE OR DELETE ON "StandardSetVersions" FOR EACH ROW EXECUTE FUNCTION teachwell_guard_published_row();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_published_standard_set_versions ON "StandardSetVersions";
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_StandardSetVersions_StandardSetVersionId",
                table: "Reviews");

            migrationBuilder.DropTable(
                name: "StandardSetVersions");

            migrationBuilder.DropTable(
                name: "StandardSets");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_StandardSetVersionId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "StandardSetVersionId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Courses");
        }
    }
}
