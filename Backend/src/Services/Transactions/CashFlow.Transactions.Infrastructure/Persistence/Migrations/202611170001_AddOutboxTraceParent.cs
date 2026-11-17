using System.Diagnostics.CodeAnalysis;
using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CashFlow.Transactions.Infrastructure.Persistence.Migrations;

/// <summary>Non-destructive W3C trace context storage for the transactional Outbox.</summary>
[ExcludeFromCodeCoverage]
[DbContext(typeof(TransactionsDbContext))]
[Migration("202611170001_AddOutboxTraceParent")]
public sealed class AddOutboxTraceParent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            table: "outbox_messages",
            type: "character varying(55)",
            maxLength: 55,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "trace_parent",
            table: "outbox_messages");
    }
}
