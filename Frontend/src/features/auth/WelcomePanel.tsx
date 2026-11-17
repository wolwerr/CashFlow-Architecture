export function WelcomePanel() {
  return <section className="panel welcome">
    <h2>Acesse sua conta</h2>
    <p>Use o login seguro do Keycloak para consultar e registrar movimentações financeiras.</p>
    <p className="muted">Os tokens são mantidos somente na memória do navegador. Se recarregar, entre novamente.</p>
  </section>;
}
