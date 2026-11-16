import type { CashFlowController } from '../app/useCashFlowController';
import { TransactionForm } from './TransactionForm';
import { BalancePanel } from './BalancePanel';
import { HistoryPanel } from './HistoryPanel';

export function FinancialDashboard({ controller }: { controller: CashFlowController }) {
  return <>
    <div className="columns">
      <TransactionForm controller={controller} />
      <BalancePanel controller={controller} />
    </div>
    <HistoryPanel controller={controller} />
  </>;
}
