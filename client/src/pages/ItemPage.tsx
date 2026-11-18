import { useState } from 'react';
import { api, ApiError, describeError, NoResponseError } from '../api';
import { catalogHref, signInHref } from '../nav';
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
  // A student's own active reservation for this item, so we can point to it instead of a dead end.
  const mine = useLoad(
    () => (user?.role === 'Student'
      ? api.myReservations().then((list) => list.find((r) => r.equipmentItemId === itemId && r.isActive) ?? null)
      : Promise.resolve(null)),
    [itemId, user?.id],
  );
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
          const list = await api.myReservations();
          const found = list.find((r) => r.equipmentItemId === itemId && r.isActive);
          setOutcome(found ? { kind: 'success', reservation: found } : { kind: 'error', message: 'The reservation was not recorded. You can try again.' });
        } catch {
          setOutcome({ kind: 'error', message: 'We could not confirm whether the reservation was recorded. Check My reservations before trying again.' });
        }
      } else {
        setOutcome({ kind: 'error', message: describeError(error) });
      }
    } finally {
      // BR-RES-007: always show availability from the server after an attempt, never a guess.
      void item.reload();
      void mine.reload();
    }
  };

  const back = <p className="back"><a href={catalogHref()}>← Back to catalog</a></p>;

  if (item.status === 'loading' && !item.data) return <Loading what="item details" />;
  if (item.status === 'error') {
    const missing = item.error instanceof ApiError && item.error.problem.status === 404;
    return missing
      ? <>{back}<Notice kind="info">This item does not exist or is no longer listed.</Notice></>
      : <ErrorMessage error={item.error} onRetry={item.reload} />;
  }

  const detail = item.data!;
  const myReservation = outcome.kind === 'success' ? outcome.reservation : mine.data;
  const isStudent = user?.role === 'Student';

  return (
    <section aria-labelledby="item-title">
      {back}
      <div className="card item-card">
        <div className="item-head">
          <div>
            <h1 id="item-title">{detail.name}</h1>
            <p className="muted subtitle">{detail.category} · <code>{detail.assetCode}</code></p>
          </div>
          <AvailabilityBadge available={detail.isAvailable} />
        </div>
        <p>{detail.description}</p>

        {outcome.kind === 'success' && <Notice kind="success">Reserved. Pick it up at the lending desk.</Notice>}
        {outcome.kind === 'error' && <Notice kind="error">{outcome.message}</Notice>}
        {outcome.kind === 'checking' && <Notice kind="info">No response from the server. Checking whether your reservation was recorded…</Notice>}

        <div className="item-action">
          {user === null && (
            <p><a className="btn-link-strong" href={signInHref()}>Sign in as a student</a> to reserve this item. You'll come straight back here.</p>
          )}

          {isStudent && myReservation && (
            <p>
              You have reserved this item.{' '}
              <a href={`#/reservations/${myReservation.id}`}>View your reservation</a>
            </p>
          )}

          {isStudent && !myReservation && detail.isAvailable && (
            <button type="button" onClick={reserve} disabled={outcome.kind === 'working' || outcome.kind === 'checking'}>
              {outcome.kind === 'working' ? 'Reserving…' : 'Reserve this item'}
            </button>
          )}

          {isStudent && !myReservation && !detail.isAvailable && mine.status !== 'loading' && (
            <p className="muted">
              Someone else has reserved or borrowed this item, so it can't be reserved right now.
              Check back later, or <a href={catalogHref()}>browse other equipment</a>.
            </p>
          )}

          {user?.role === 'Staff' && (
            <p className="muted">Staff record pickups and returns from the <a href="#/staff">staff queue</a>.</p>
          )}
        </div>
      </div>
    </section>
  );
}
