import { useEffect, useState } from 'react';

/** A parsed hash route, e.g. "#/items/42?next=/mine" → path "/items/42", query {next: "/mine"}. */
export interface Route {
  path: string;
  query: URLSearchParams;
}

const read = (): Route => {
  const raw = window.location.hash.replace(/^#/, '') || '/';
  const [path, qs = ''] = raw.split('?', 2);
  return { path: path || '/', query: new URLSearchParams(qs) };
};

export function useHashRoute(): Route {
  const [route, setRoute] = useState(read);
  useEffect(() => {
    const onChange = () => setRoute(read());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return route;
}

export const navigate = (path: string) => {
  window.location.hash = path;
};

/** Builds "#/path?a=1" from a path and a set of parameters, leaving out empty values. */
export function href(path: string, params: Record<string, string | number | boolean | undefined> = {}): string {
  const q = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '' && value !== false) q.set(key, String(value));
  }
  const s = q.toString();
  return `#${path}${s ? `?${s}` : ''}`;
}

// The catalog keeps its filters and page in the URL. Remembering the last one lets
// "Back to catalog" return to exactly where the user was (search, category, page).
let lastCatalog = '#/';
export const rememberCatalog = (hash: string) => { lastCatalog = hash; };
export const catalogHref = () => lastCatalog;

/** Sign-in link that brings the user back to the current page afterwards. */
export function signInHref(): string {
  const here = window.location.hash.replace(/^#/, '') || '/';
  return here.startsWith('/login') ? '#/login' : href('/login', { next: here });
}

/** Only same-app paths are accepted as a post-sign-in destination (no open redirects). */
export function safeNext(next: string | null): string | null {
  return next && next.startsWith('/') && !next.startsWith('//') ? next : null;
}
