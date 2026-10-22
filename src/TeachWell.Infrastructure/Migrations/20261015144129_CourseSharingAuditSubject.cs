using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CourseSharingAuditSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubjectUserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubjectUserId",
                table: "AuditLogs");
        }
    }
}
