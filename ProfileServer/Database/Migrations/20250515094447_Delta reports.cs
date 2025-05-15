using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProfileServer.Database.Migrations
{
    /// <inheritdoc />
    public partial class Deltareports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "averageFrametime",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "maxFrameTime",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<ulong>(
                name: "messageId",
                table: "PerformanceReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<float>(
                name: "percentile95",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "percentile99",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "averageFrametime",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "maxFrameTime",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "messageId",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "percentile95",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "percentile99",
                table: "PerformanceReports");
        }
    }
}
