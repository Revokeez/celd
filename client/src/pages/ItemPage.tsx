import { useState } from 'react';
import { api, ApiError, describeError, NoResponseError } from '../api';
import type { CurrentUser, Reservation } from '../types';
import { AvailabilityBadge, ErrorMessage, Loading, Notice, useLoad } from '../components/Status';

type Outcome =
  | { kind: 'idle' }
  | { kind: 'working' }
  | { kind: 'success'; reservation: Reservation }
  | { kind: 'error'; message: string }
  | { kind: 'checking' };

/** UC-001 steps 5-7 and UC-002 Reserve Equipment. */
export function ItemPage({ itemId, user }: { itemId: string; user: CurrentUser | null }) {
  const item = useLoad(() => api.getEquipment(itemId), [itemId]);
  const [outcome, setOutcome] = useState<Outcome>({ kind: 'idle' });

  const reserve = async () => {
    setOutcome({ kind: 'working' });
    try {
      const reservation = await api.reserve(itemId);
      setOutcome({ kind: 'success', reservation });
    } catch (error) {
      if (error instanceof NoResponseError) {
        // UC-002 E7.1: we do not know. Ask the server what really happened before saying anything.
        setOutcome({ kind: 'checking' });
        try {
          const mine = await api.myReservations();
          const found = mine.find((r) => r.equipmentItemId === itemId && r.isActive);
          setOutcome(found ? { kind: 'success', reservation: found } : { kind: 'error', message: 'The reservation was not recorded. You can try again.' });
        } catch {
          setOutcome({ kind: 'error', message: 'We could not confirm whether the reservation was recorded. Check My Reservations before trying again.' });
        }
      } else {
        setOutcome({ kind: 'error', message: describeError(error) });
      }
    } finally {
      // BR-RES-007: always show availability from the server after an attempt, never a guess.
      void item.reload();
    }
  };

  if (item.status === 'loading' && !item.data) return <Loading what="item details" />;
  if (item.status === 'error') {
    const missing = item.error instanceof ApiError && item.error.problem.status === 404;
    return missing
      ? <Notice kind="info">This item does not exist or is no longer listed. <a href="#/">Back to the catalog</a></Notice>
      : <ErrorMessage error={item.error} onRetry={item.reload} />;
  }

  const detail = item.data!;
  return (
    <section aria-labelledby="item-title">
      <p><a href="#/">← Back to catalog</a></p>
      <h1 id="item-title">{detail.name}</h1>
      <dl className="facts">
        <dt>Asset code</dt><dd><code>{detail.assetCode}</code></dd>
        <dt>Category</dt><dd>{detail.category}</dd>
        <dt>Availability</dt><dd><AvailabilityBadge available={detail.isAvailable} /></dd>
      </dl>
      <p>{detail.description}</p>

      {outcome.kind === 'success' && (
        <Notice kind="success">
          Reserved. <a href={`#/reservations/${outcome.reservation.id}`}>View your reservation</a>
        </Notice>
      )}
      {outcome.kind === 'error' && <Notice kind="error">{outcome.message}</Notice>}
      {outcome.kind === 'checking' && <Notice kind="info">No response from the server. Checking whether your reservation was recorded…</Notice>}

      {user === null && <p><a href="#/login">Sign in as a student</a> to reserve this item.</p>}
      {user?.role === 'Student' && detail.isAvailable && outcome.kind !== 'success' && (
        <button type="button" onClick={reserve} disabled={outcome.kind === 'working' || outcome.kind === 'checking'}>
          {outcome.kind === 'working' ? 'Reserving…' : 'Reserve'}
        </button>
      )}
    </section>
  );
}
