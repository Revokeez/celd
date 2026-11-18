import { useState } from 'react';
import { api } from '../api';
import { signInHref } from '../nav';
import type { CurrentUser } from '../types';
import { Empty, ErrorMessage, ItemCell, Loading, Notice, StatusBadge, formatTime, useLoad } from '../components/Status';

/** Past reservations shown before "Show all", so current ones stay easy to find. */
const PAST_PREVIEW = 10;

/** UC-003 steps 1-3. Active and finished reservations are listed separately (FR-RES-002). */
export function MyReservationsPage({ user }: { user: CurrentUser | null }) {
  const list = useLoad(() => (user?.role === 'Student' ? api.myReservations() : Promise.resolve([])), [user?.id]);
  const [showAllPast, setShowAllPast] = useState(false);

  if (user?.role !== 'Student') return <Notice kind="info"><a href={signInHref()}>Sign in as a student</a> to see your reservations.</Notice>;
  if (list.status === 'loading' && !list.data) return <Loading what="your reservations" />;
  if (list.status === 'error') return <ErrorMessage error={list.error} onRetry={list.reload} />;

  const all = list.data!;
  const active = all.filter((r) => r.isActive);
  const finished = all.filter((r) => !r.isActive);
  const hiddenPast = showAllPast ? 0 : Math.max(0, finished.length - PAST_PREVIEW);
  const visiblePast = hiddenPast > 0 ? finished.slice(0, PAST_PREVIEW) : finished;

  const table = (rows: typeof all, title: string, total: number, hint?: string) => (
    <>
      <h2>{title} <span className="count">{total}</span></h2>
      {hint && <p className="muted">{hint}</p>}
      <div className="table-wrap">
        <table className="data">
          <caption className="sr-only">{title}</caption>
          <thead><tr><th scope="col">Item</th><th scope="col">Status</th><th scope="col">Reserved on</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td><ItemCell name={r.itemName} assetCode={r.assetCode} href={`#/reservations/${r.id}`} /></td>
                <td><StatusBadge status={r.status} /></td>
                <td className="nowrap">{formatTime(r.createdAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );

  return (
    <section aria-labelledby="mine-title">
      <h1 id="mine-title">My reservations</h1>
      {all.length === 0 && <Empty>You have no reservations yet. <a href="#/">Browse the catalog</a> to reserve equipment.</Empty>}
      {all.length > 0 && active.length === 0 && (
        <Empty>You have nothing reserved or borrowed right now. <a href="#/">Browse the catalog</a>.</Empty>
      )}
      {active.length > 0 && table(active, 'Current', active.length, 'Open a reservation to see its details or cancel it.')}
      {finished.length > 0 && table(visiblePast, 'Past reservations', finished.length)}
      {finished.length > PAST_PREVIEW && (
        <p className="show-more">
          <button type="button" className="btn-secondary" aria-expanded={showAllPast} onClick={() => setShowAllPast((v) => !v)}>
            {showAllPast ? `Show only the latest ${PAST_PREVIEW}` : `Show all ${finished.length} past reservations`}
          </button>
        </p>
      )}
    </section>
  );
}
