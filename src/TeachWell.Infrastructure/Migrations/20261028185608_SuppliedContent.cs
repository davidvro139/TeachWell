using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeachWell.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SuppliedContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SuppliedAt",
                table: "Elements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuppliedBy",
                table: "Elements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuppliedAt",
                table: "Elements");

            migrationBuilder.DropColumn(
                name: "SuppliedBy",
                table: "Elements");
        }
    }
}
