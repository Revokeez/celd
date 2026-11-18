import type {
  CurrentUser, EquipmentDetail, EquipmentSummary, HistoryEvent, PagedResult, Problem, Reservation, ReservationStatus,
} from './types';

/** The server answered with an error (FR-ERR-001 problem body). */
export class ApiError extends Error {
  constructor(public readonly problem: Problem) {
    super(problem.detail);
  }
}

/**
 * No response arrived (network drop, timeout). The operation MAY have succeeded on the server,
 * so callers must re-read authoritative state before telling the user anything (UC-002 E7.1).
 */
export class NoResponseError extends Error {}

let csrfToken: string | null = null;

/** The token is bound to who is signed in, so it is refreshed after sign-in and sign-out. */
export async function refreshCsrf(): Promise<void> {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
  csrfToken = ((await response.json()) as { token: string }).token;
}

async function request<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  if (method !== 'GET' && csrfToken === null) {
    await refreshCsrf();
  }

  const headers: Record<string, string> = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET' && csrfToken) headers['X-CSRF-TOKEN'] = csrfToken;

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      credentials: 'same-origin',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new NoResponseError('No response from the server.');
  }

  if (response.status === 204) return undefined as T;

  if (!response.ok) {
    let problem: Problem;
    try {
      const json = await response.json();
      problem = {
        status: response.status,
        category: json.category ?? 'ServerError',
        errorCode: json.errorCode ?? 'unknown',
        detail: json.detail ?? json.title ?? `Request failed (${response.status}).`,
      };
    } catch {
      problem = { status: response.status, category: 'ServerError', errorCode: 'unknown', detail: `Request failed (${response.status}).` };
    }
    throw new ApiError(problem);
  }

  return (await response.json()) as T;
}

const query = (params: Record<string, string | undefined>) => {
  const q = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) if (value) q.set(key, value);
  const s = q.toString();
  return s ? `?${s}` : '';
};

export const api = {
  listEquipment: (search?: string, category?: string, page = 1, pageSize?: number, availableOnly = false) =>
    request<PagedResult<EquipmentSummary>>(
      'GET',
      `/api/equipment${query({
        search,
        category,
        available: availableOnly ? 'true' : undefined,
        page: String(page),
        pageSize: pageSize?.toString(),
      })}`,
    ),
  categories: () => request<string[]>('GET', '/api/equipment/categories'),
  getEquipment: (id: string) => request<EquipmentDetail>('GET', `/api/equipment/${id}`),

  me: () => request<CurrentUser>('GET', '/api/auth/me'),
  async login(userName: string, password: string) {
    await refreshCsrf();
    const user = await request<CurrentUser>('POST', '/api/auth/login', { userName, password });
    await refreshCsrf();
    return user;
  },
  async logout() {
    await request<void>('POST', '/api/auth/logout');
    await refreshCsrf();
  },

  reserve: (equipmentItemId: string) => request<Reservation>('POST', '/api/reservations', { equipmentItemId }),
  myReservations: () => request<Reservation[]>('GET', '/api/reservations/mine'),
  getReservation: (id: string) => request<Reservation>('GET', `/api/reservations/${id}`),
  history: (id: string) => request<HistoryEvent[]>('GET', `/api/reservations/${id}/history`),
  cancel: (id: string) => request<Reservation>('POST', `/api/reservations/${id}/cancel`),
  recordPickup: (id: string) => request<Reservation>('POST', `/api/reservations/${id}/pickup`),
  recordReturn: (id: string) => request<Reservation>('POST', `/api/reservations/${id}/return`),
  staffQueue: (status?: ReservationStatus) => request<Reservation[]>('GET', `/api/staff/reservations${query({ status })}`),
};

/** Turns any thrown value into text that is safe and useful to show. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.problem.detail;
  if (error instanceof NoResponseError) return 'The server did not respond. Check your connection and try again.';
  return 'Something went wrong. Please try again.';
}
