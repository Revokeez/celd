import { useEffect, useState, type FormEvent } from 'react';
import { api, describeError, NoResponseError } from '../api';
import { href, rememberCatalog } from '../nav';
import type { CurrentUser } from '../types';
import { AvailabilityBadge, Empty, ErrorMessage, ItemCell, Loading, Notice, useLoad } from '../components/Status';

const PAGE_SIZE = 10;

type Filters = { search: string; category: string; available: boolean; page: number };

const fromQuery = (q: URLSearchParams): Filters => ({
  search: q.get('search') ?? '',
  category: q.get('category') ?? '',
  available: q.get('available') === '1',
  page: Math.max(1, Number(q.get('page')) || 1),
});

/** Filters and page live in the URL, so Back, refresh and shared links all keep the user's place. */
const go = (f: Filters) => {
  window.location.hash = href('/', {
    search: f.search,
    category: f.category,
    available: f.available ? 1 : undefined,
    page: f.page > 1 ? f.page : undefined,
  });
};

type RowMessage = { kind: 'success' | 'error' | 'info'; text: string; reservationId?: string };

/** UC-001 Find Equipment. Public. Students can also reserve straight from the list (UC-002). */
export function CatalogPage({ query, user }: { query: URLSearchParams; user: CurrentUser | null }) {
  const filters = fromQuery(query);
  const key = query.toString();
  const [draftSearch, setDraftSearch] = useState(filters.search);
  const [reserving, setReserving] = useState<string | null>(null);
  const [message, setMessage] = useState<RowMessage | null>(null);

  // Keep the text box in step when the URL changes (Back button, Clear filters).
  useEffect(() => { setDraftSearch(filters.search); }, [filters.search]);
  useEffect(() => { rememberCatalog(window.location.hash || '#/'); }, [key]);

  const categories = useLoad(() => api.categories(), []);
  const items = useLoad(
    () => api.listEquipment(filters.search, filters.category, filters.page, PAGE_SIZE, filters.available),
    [key],
  );

  const submit = (event: FormEvent) => {
    event.preventDefault();
    go({ ...filters, search: draftSearch.trim(), page: 1 }); // A new search always starts from page 1.
  };

  const hasFilters = filters.search !== '' || filters.category !== '' || filters.available;

  const reserve = async (itemId: string, name: string) => {
    setReserving(itemId);
    setMessage(null);
    try {
      const r = await api.reserve(itemId);
      setMessage({ kind: 'success', text: `${name} is reserved for you.`, reservationId: r.id });
    } catch (error) {
      setMessage(error instanceof NoResponseError
        ? { kind: 'info', text: 'The server did not respond. Check My reservations before trying again.' }
        : { kind: 'error', text: describeError(error) });
    } finally {
      setReserving(null);
      void items.reload(); // Always show availability from the server, never a guess (BR-RES-007).
    }
  };

  const result = items.data;
  const first = result ? (result.page - 1) * result.pageSize + 1 : 0;
  const last = result ? first + result.items.length - 1 : 0;
  const isStudent = user?.role === 'Student';

  return (
    <section aria-labelledby="catalog-title">
      <h1 id="catalog-title">Equipment catalog</h1>

      <form className="filters card" onSubmit={submit} role="search">
        <label className="grow">
          Name contains
          <span className="input-with-button">
            <input
              type="search"
              value={draftSearch}
              placeholder="e.g. camera"
              onChange={(e) => setDraftSearch(e.target.value)}
            />
            <button type="submit">Search</button>
          </span>
        </label>
        <label>
          Category
          <select
            value={filters.category}
            onChange={(e) => go({ ...filters, search: draftSearch.trim(), category: e.target.value, page: 1 })}
          >
            <option value="">All categories</option>
            {categories.data?.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
        <label className="checkbox">
          <input
            type="checkbox"
            checked={filters.available}
            onChange={(e) => go({ ...filters, search: draftSearch.trim(), available: e.target.checked, page: 1 })}
          />
          Available only
        </label>
        {hasFilters && (
          <button type="button" className="btn-secondary" onClick={() => { setDraftSearch(''); go({ search: '', category: '', available: false, page: 1 }); }}>
            Clear filters
          </button>
        )}
      </form>

      {message && (
        <Notice kind={message.kind}>
          {message.text}{' '}
          {message.reservationId && <a href={`#/reservations/${message.reservationId}`}>View reservation</a>}
        </Notice>
      )}

      {items.status === 'loading' && !result && <Loading what="equipment" />}
      {items.status === 'error' && <ErrorMessage error={items.error} onRetry={items.reload} />}
      {result && result.totalCount === 0 && (
        <Empty>
          No equipment matches your search.{' '}
          {hasFilters && <button type="button" className="btn-link" onClick={() => go({ search: '', category: '', available: false, page: 1 })}>Clear filters</button>}
        </Empty>
      )}
      {result && result.totalCount > 0 && (
        <>
          <p className="muted result-count" role="status" aria-live="polite">
            Showing {first}–{last} of {result.totalCount} item{result.totalCount === 1 ? '' : 's'}
          </p>
          <div className="table-wrap">
            <table className="data">
              <caption className="sr-only">Equipment items and their availability</caption>
              <thead>
                <tr>
                  <th scope="col">Item</th>
                  <th scope="col">Category</th>
                  <th scope="col">Availability</th>
                  {isStudent && <th scope="col"><span className="sr-only">Action</span></th>}
                </tr>
              </thead>
              <tbody>
                {result.items.map((item) => (
                  <tr key={item.id}>
                    <td><ItemCell name={item.name} assetCode={item.assetCode} href={`#/items/${item.id}`} /></td>
                    <td>{item.category}</td>
                    <td><AvailabilityBadge available={item.isAvailable} /></td>
                    {isStudent && (
                      <td className="row-action">
                        {item.isAvailable && (
                          <button
                            type="button"
                            className="btn-small"
                            onClick={() => reserve(item.id, item.name)}
                            disabled={reserving !== null}
                            aria-label={`Reserve ${item.name}`}
                          >
                            {reserving === item.id ? 'Reserving…' : 'Reserve'}
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {result.totalPages > 1 && (
            <nav className="pager" aria-label="Catalog pages">
              <button type="button" className="btn-secondary" onClick={() => go({ ...filters, page: result.page - 1 })} disabled={result.page <= 1}>
                ‹ Previous
              </button>
              <span>Page {result.page} of {result.totalPages}</span>
              <button type="button" className="btn-secondary" onClick={() => go({ ...filters, page: result.page + 1 })} disabled={result.page >= result.totalPages}>
                Next ›
              </button>
            </nav>
          )}
        </>
      )}
    </section>
  );
}
