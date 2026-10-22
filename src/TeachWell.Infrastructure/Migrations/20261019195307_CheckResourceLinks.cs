using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckResourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckResourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkshopId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckResourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckResourceLinks_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CheckResourceLinks_KnowledgeArticles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "KnowledgeArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckResourceLinks_Workshops_WorkshopId",
                        column: x => x.WorkshopId,
                        principalTable: "Workshops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckResourceLinks_ArticleId",
                table: "CheckResourceLinks",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckResourceLinks_CreatedBy",
                table: "CheckResourceLinks",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CheckResourceLinks_TenantId_CheckCode",
                table: "CheckResourceLinks",
                columns: new[] { "TenantId", "CheckCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckResourceLinks_WorkshopId",
                table: "CheckResourceLinks",
                column: "WorkshopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckResourceLinks");
        }
    }
}
