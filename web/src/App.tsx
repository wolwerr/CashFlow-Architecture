import { useCashFlowController } from './features/app/useCashFlowController';
import { AppHeader } from './features/auth/AppHeader';
import { WelcomePanel } from './features/auth/WelcomePanel';
import { AdminPanel } from './features/merchants/AdminPanel';
import { FinancialDashboard } from './features/finance/FinancialDashboard';

export default function App() {
  const controller = useCashFlowController();
  const { currentToken, notice, canManageMerchants } = controller;

  return <main className="app">
    <AppHeader controller={controller} />
    <p className="intro">Lançamentos, histórico e saldos diários, com autorização por comerciante.</p>
    {notice && <div role="status" className={notice.isError ? 'notice error' : 'notice'}>{notice.text}</div>}
    {!currentToken ? <WelcomePanel /> : <>
      {canManageMerchants && <AdminPanel controller={controller} />}
      <FinancialDashboard controller={controller} />
    </>}
    <footer>Ambiente local · Keycloak · PostgreSQL · RabbitMQ</footer>
  </main>;
}
