import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import App from './App';

describe('CashFlow initial screen', () => {
  it('shows the application title and makes clear that financial operations are not ready', () => {
    const html = renderToStaticMarkup(<App />);
    expect(html).toContain('<h1>CashFlow</h1>');
    expect(html).toContain('As operações financeiras estarão disponíveis após implementar autenticação');
  });
});
