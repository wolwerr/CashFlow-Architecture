using System.Diagnostics.CodeAnalysis;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CashFlow.Transactions.Infrastructure.Persistence.Migrations;

/// <summary>Initial local PostgreSQL schema for the four Transactions-owned tables.</summary>
[ExcludeFromCodeCoverage]
[DbContext(typeof(TransactionsDbContext))]
[Migration("202611160001_InitialTransactionsPersistence")]
public sealed class InitialTransactionsPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "transactions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<short>(type: "smallint", nullable: false),
                amount = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
                reference_date = table.Column<DateOnly>(type: "date", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transactions", x => x.id);
                table.CheckConstraint("ck_transactions_amount", "amount > 0");
                table.CheckConstraint("ck_transactions_type", "type IN (1, 2)");
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                schema_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.event_id);
                table.CheckConstraint("ck_outbox_attempts", "attempt_count >= 0");
            });

        migrationBuilder.CreateTable(
            name: "transaction_audits",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_transaction_audits", x => x.id));

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                request_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_idempotency_records", x => new { x.merchant_id, x.request_key }));

        migrationBuilder.CreateIndex(
            name: "IX_transactions_merchant_id_reference_date_id",
            table: "transactions",
            columns: new[] { "merchant_id", "reference_date", "id" });
        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_published_at_utc_created_at_utc",
            table: "outbox_messages",
            columns: new[] { "published_at_utc", "created_at_utc" });
        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_merchant_id_transaction_id",
            table: "outbox_messages",
            columns: new[] { "merchant_id", "transaction_id" });
        migrationBuilder.CreateIndex(
            name: "IX_transaction_audits_merchant_id_occurred_at_utc",
            table: "transaction_audits",
            columns: new[] { "merchant_id", "occurred_at_utc" });
        migrationBuilder.CreateIndex(
            name: "IX_transaction_audits_transaction_id",
            table: "transaction_audits",
            column: "transaction_id");
        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_transaction_id",
            table: "idempotency_records",
            column: "transaction_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "idempotency_records");
        migrationBuilder.DropTable(name: "outbox_messages");
        migrationBuilder.DropTable(name: "transaction_audits");
        migrationBuilder.DropTable(name: "transactions");
    }
}
