using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CourseReviewAndPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubjectElementId",
                table: "ReviewFindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewHtml",
                table: "ElementSnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewHtml",
                table: "Elements",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewFindings_SubjectElementId",
                table: "ReviewFindings",
                column: "SubjectElementId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewFindings_Elements_SubjectElementId",
                table: "ReviewFindings",
                column: "SubjectElementId",
                principalTable: "Elements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReviewFindings_Elements_SubjectElementId",
                table: "ReviewFindings");

            migrationBuilder.DropIndex(
                name: "IX_ReviewFindings_SubjectElementId",
                table: "ReviewFindings");

            migrationBuilder.DropColumn(
                name: "SubjectElementId",
                table: "ReviewFindings");

            migrationBuilder.DropColumn(
                name: "PreviewHtml",
                table: "ElementSnapshots");

            migrationBuilder.DropColumn(
                name: "PreviewHtml",
                table: "Elements");
        }
    }
}
