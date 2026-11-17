/** Financial reference dates follow the configured business timezone. */
export function businessDate(now = new Date()): string {
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(now);
  const part = (name: string) => date.find(x => x.type === name)?.value || '';
  return [part('year'), part('month'), part('day')].join('-');
}
