using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CashFlow.Consolidation.Infrastructure.Persistence.Migrations;

[ExcludeFromCodeCoverage]
[DbContext(typeof(ConsolidationDbContext))]
[Migration("202611160002_InitialConsolidation")]
public sealed class InitialConsolidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("daily_balances", columns: table => new
        {
            merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
            reference_date = table.Column<DateOnly>(type: "date", nullable: false),
            credit_total = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
            debit_total = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
            updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_daily_balances", x => new { x.merchant_id, x.reference_date });
            table.CheckConstraint("ck_daily_balances_credit", "credit_total >= 0");
            table.CheckConstraint("ck_daily_balances_debit", "debit_total >= 0");
        });

        migrationBuilder.CreateTable("processed_events", columns: table => new
        {
            event_id = table.Column<Guid>(type: "uuid", nullable: false),
            transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
            merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
            processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_processed_events", x => x.event_id));
        migrationBuilder.CreateIndex("IX_processed_events_merchant_id_processed_at_utc",
            "processed_events", new[] { "merchant_id", "processed_at_utc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("processed_events");
        migrationBuilder.DropTable("daily_balances");
    }
}
