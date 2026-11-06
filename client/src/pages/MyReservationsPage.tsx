import { api } from '../api';
import type { CurrentUser } from '../types';
import { Empty, ErrorMessage, Loading, Notice, StatusBadge, formatTime, useLoad } from '../components/Status';

/** UC-003 steps 1-3. Active and finished reservations are listed separately (FR-RES-002). */
export function MyReservationsPage({ user }: { user: CurrentUser | null }) {
  const list = useLoad(() => (user?.role === 'Student' ? api.myReservations() : Promise.resolve([])), [user?.id]);

  if (user?.role !== 'Student') return <Notice kind="info">Sign in as a student to see your reservations.</Notice>;
  if (list.status === 'loading') return <Loading what="your reservations" />;
  if (list.status === 'error') return <ErrorMessage error={list.error} onRetry={list.reload} />;

  const all = list.data!;
  const active = all.filter((r) => r.isActive);
  const finished = all.filter((r) => !r.isActive);

  const table = (rows: typeof all, caption: string) => (
    <table>
      <caption>{caption}</caption>
      <thead><tr><th scope="col">Item</th><th scope="col">Status</th><th scope="col">Created</th></tr></thead>
      <tbody>
        {rows.map((r) => (
          <tr key={r.id}>
            <td><a href={`#/reservations/${r.id}`}>{r.itemName}</a> <code>{r.assetCode}</code></td>
            <td><StatusBadge status={r.status} /></td>
            <td>{formatTime(r.createdAtUtc)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );

  return (
    <section aria-labelledby="mine-title">
      <h1 id="mine-title">My reservations</h1>
      {all.length === 0 && <Empty>You have no reservations yet. <a href="#/">Browse the catalog</a>.</Empty>}
      {active.length > 0 && table(active, 'Active')}
      {finished.length > 0 && table(finished, 'Completed or cancelled')}
    </section>
  );
}
