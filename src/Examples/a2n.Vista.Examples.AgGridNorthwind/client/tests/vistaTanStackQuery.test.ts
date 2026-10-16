import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  TANSTACK_ROUTE_SUFFIX,
  TanStackTableRequestError,
  buildTanStackTableRequest,
  fetchTanStackTablePage,
  tanStackEndpoint,
  type TanStackTableQueryState,
} from '../src/vistaTanStackQuery.js';

const ENDPOINT = '/api/views/vProductCategory/tanstack';

const STATE: TanStackTableQueryState = {
  pagination: { pageIndex: 1, pageSize: 20 },
  sorting: [
    { id: 'CategoryName', desc: false },
    { id: 'ProductName', desc: true },
  ],
  columnFilters: [
    { id: 'ProductName', value: 'chai' },
    { id: 'UnitPrice', value: { min: 10, max: 40 } },
    { id: 'Discontinued', value: { op: 'equals', value: false } },
  ],
  globalFilter: 'tea',
  columnVisibility: { Discontinued: false, actions: false },
};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('tanStackEndpoint', () => {
  it('appends the adapter suffix', () => {
    expect(tanStackEndpoint('/api/views/vProductCategory')).toBe(ENDPOINT);
    expect(tanStackEndpoint('/api/views/vProductCategory/')).toBe(ENDPOINT);
    expect(TANSTACK_ROUTE_SUFFIX).toBe('tanstack');
  });
});

describe('buildTanStackTableRequest', () => {
  it('posts the table-state slices under their TanStack names', () => {
    const body = buildTanStackTableRequest(STATE);

    expect(body).toEqual({
      pagination: { pageIndex: 1, pageSize: 20 },
      sorting: [
        { id: 'CategoryName', desc: false },
        { id: 'ProductName', desc: true },
      ],
      columnFilters: [
        { id: 'ProductName', value: 'chai' },
        { id: 'UnitPrice', value: { min: 10, max: 40 } },
        { id: 'Discontinued', value: { op: 'equals', value: false } },
      ],
      globalFilter: 'tea',
      columnVisibility: { Discontinued: false, actions: false },
    });
  });

  it('copies sorting, filters, and visibility instead of aliasing them', () => {
    const body = buildTanStackTableRequest(STATE);
    expect(body.sorting).not.toBe(STATE.sorting);
    expect(body.columnFilters).not.toBe(STATE.columnFilters);
    expect(body.columnVisibility).not.toBe(STATE.columnVisibility);
  });
});

describe('fetchTanStackTablePage', () => {
  it('POSTs the state as JSON and returns rows, rowCount, and pageCount', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({
      rows: [{ productName: 'Chai' }],
      rowCount: 8,
      pageCount: 1,
    }));

    const page = await fetchTanStackTablePage<{ productName: string }>(ENDPOINT, STATE, fetchImpl);

    expect(page.rowCount).toBe(8);
    expect(page.pageCount).toBe(1);
    expect(page.rows).toEqual([{ productName: 'Chai' }]);

    expect(fetchImpl).toHaveBeenCalledOnce();
    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe(ENDPOINT);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toMatchObject({
      pagination: { pageIndex: 1, pageSize: 20 },
      globalFilter: 'tea',
    });
  });

  it('throws on a non-2xx response and does not parse the body as rows', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ rows: [] }, { ok: false, status: 400, statusText: 'Bad Request' }));

    await expect(fetchTanStackTablePage(ENDPOINT, STATE, fetchImpl)).rejects.toBeInstanceOf(TanStackTableRequestError);
  });
});

function jsonResponse(
  body: unknown,
  init?: { ok?: boolean; status?: number; statusText?: string },
): Response {
  return {
    ok: init?.ok ?? true,
    status: init?.status ?? 200,
    statusText: init?.statusText ?? 'OK',
    json: async () => body,
  } as unknown as Response;
}
