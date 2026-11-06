import { useState, type FormEvent } from 'react';
import { api } from '../api';
import { AvailabilityBadge, Empty, ErrorMessage, Loading, useLoad } from '../components/Status';

/** UC-001 Find Equipment. Public. */
export function CatalogPage() {
  const [draftSearch, setDraftSearch] = useState('');
  const [draftCategory, setDraftCategory] = useState('');
  const [filters, setFilters] = useState({ search: '', category: '' });

  const categories = useLoad(() => api.categories(), []);
  const items = useLoad(() => api.listEquipment(filters.search, filters.category), [filters]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setFilters({ search: draftSearch.trim(), category: draftCategory });
  };

  return (
    <section aria-labelledby="catalog-title">
      <h1 id="catalog-title">Equipment catalog</h1>

      <form className="filters" onSubmit={submit} role="search">
        <label>
          Name contains
          <input type="search" value={draftSearch} onChange={(e) => setDraftSearch(e.target.value)} />
        </label>
        <label>
          Category
          <select value={draftCategory} onChange={(e) => setDraftCategory(e.target.value)}>
            <option value="">All categories</option>
            {categories.data?.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
        <button type="submit">Search</button>
      </form>

      {items.status === 'loading' && <Loading what="equipment" />}
      {items.status === 'error' && <ErrorMessage error={items.error} onRetry={items.reload} />}
      {items.status === 'ready' && items.data!.length === 0 && (
        <Empty>No equipment matches your search. Try a different name or category.</Empty>
      )}
      {items.status === 'ready' && items.data!.length > 0 && (
        <table>
          <caption className="sr-only">Equipment items and their availability</caption>
          <thead>
            <tr><th scope="col">Name</th><th scope="col">Category</th><th scope="col">Asset code</th><th scope="col">Availability</th></tr>
          </thead>
          <tbody>
            {items.data!.map((item) => (
              <tr key={item.id}>
                <td><a href={`#/items/${item.id}`}>{item.name}</a></td>
                <td>{item.category}</td>
                <td><code>{item.assetCode}</code></td>
                <td><AvailabilityBadge available={item.isAvailable} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
