import type { Merchant } from '../../api/cashflow';
import { selectedMerchant, sortedMerchants } from './merchantPresentation';

type MerchantSelectorProps = {
  items: Merchant[];
  value: string;
  onChange: (merchantId: string) => void;
  label: string;
  ariaLabel: string;
};

export function MerchantSelector({ items, value, onChange, label, ariaLabel }: MerchantSelectorProps) {
  return <label>{label}
    <select aria-label={ariaLabel} value={selectedMerchant(items, value)}
      onChange={event => onChange(event.target.value)}>
      <option value="">Selecione...</option>
      {sortedMerchants(items).map(item => <option key={item.id} value={item.id}>
        {item.name}
      </option>)}
    </select>
  </label>;
}
