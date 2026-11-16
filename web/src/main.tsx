import React from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

function App() {
  return <main><h1>CashFlow</h1><p>Estrutura local inicial instalada.</p><p>As operações financeiras estarão disponíveis após implementar autenticação, autorização, transações e consolidação.</p></main>;
}

createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);
