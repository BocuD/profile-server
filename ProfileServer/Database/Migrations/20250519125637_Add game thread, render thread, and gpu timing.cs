using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProfileServer.Database.Migrations
{
    /// <inheritdoc />
    public partial class Addgamethreadrenderthreadandgputiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "averageGameThreadTime",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "averageGpuTime",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "averageRenderThreadTime",
                table: "PerformanceReports",
                type: "REAL",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "averageGameThreadTime",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "averageGpuTime",
                table: "PerformanceReports");

            migrationBuilder.DropColumn(
                name: "averageRenderThreadTime",
                table: "PerformanceReports");
        }
    }
}
