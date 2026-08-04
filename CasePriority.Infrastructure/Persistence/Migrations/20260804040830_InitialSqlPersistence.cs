using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasePriority.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportCases",
                columns: table => new
                {
                    CaseNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    IsExecutiveEscalation = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCases", x => x.CaseNumber);
                    table.CheckConstraint("CK_SupportCases_Severity", "[Severity] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_SupportCases_Version", "[Version] >= 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportCases");
        }
    }
}
