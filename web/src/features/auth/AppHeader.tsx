import { beginLogin, logout } from '../../auth/oidc';
import type { CashFlowController } from '../app/useCashFlowController';

export function AppHeader({ controller }: { controller: CashFlowController }) {
  const { currentToken, identity, accessStatus, authPending } = controller;
  return <header className="topbar">
      <div><div className="eyebrow">GESTÃO FINANCEIRA</div><h1>CashFlow</h1></div>
      <div className="auth-controls">
        {currentToken ? <>
          <span className="user" title={identity}>{accessStatus === 'admin' ? 'Administrador' : accessStatus === 'merchant' ? 'Comerciante' : 'Verificando perfil'}</span>
          <span className="user">{identity}</span>
          <button type="button" className="outline" onClick={logout}>Sair</button>
        </> : <button type="button" onClick={() => void beginLogin()} disabled={authPending}>
          {authPending ? 'Autenticando...' : 'Entrar com Keycloak'}
        </button>}
      </div>
    </header>;
}
