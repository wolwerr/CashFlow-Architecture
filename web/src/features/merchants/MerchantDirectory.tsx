import type { Merchant } from '../../api/cashflow';
import { merchantRoleLabel } from './merchantPresentation';

type MerchantDirectoryProps = {
  items: Merchant[];
  selectedId: string;
  onSelect: (merchantId: string) => void;
};

export function MerchantDirectory({ items, selectedId, onSelect }: MerchantDirectoryProps) {
  return <div className="merchant-directory">
    <h3>Comerciantes cadastrados</h3>
    {items.length === 0
      ? <p className="muted">Nenhum comerciante associado à sua conta.</p>
      : <div className="table-wrap">
        <table>
          <thead><tr><th>Comerciante</th><th>Seu cargo</th><th>Ação</th></tr></thead>
          <tbody>
            {items.map(item => <tr key={item.id}>
              <td>{item.name}</td>
              <td>{merchantRoleLabel(item.role)}</td>
              <td><button type="button" className="outline"
                disabled={item.id === selectedId}
                onClick={() => onSelect(item.id)}>
                {item.id === selectedId ? 'Selecionado' : 'Selecionar'}
              </button></td>
            </tr>)}
          </tbody>
        </table>
      </div>}
  </div>;
}
