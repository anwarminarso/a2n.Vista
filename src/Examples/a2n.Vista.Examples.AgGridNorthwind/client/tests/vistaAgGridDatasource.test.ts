// Feature: northwind-sample-showcase, Task 8.2 — verify the ported AG Grid datasource behavior.
//
// Validates: Requirements 1.1, 1.2, 1.3
//
// The Simple_Wiring_Page and Custom_Renderer_Page drive a Vista view through the AG Grid adapter
// endpoint (`POST {route}/aggrid`) using the ported community Infinite-Row-Model datasource in
// ../src/vistaAgGridDatasource.ts. The SERVER contract that datasource targets is proven unchanged by
// the standalone `AgGridNorthwind` self-test (the adapter-path oracle: `dotnet run -- selftest`,
// exercising BindRequest → ToQuery → executor → ToResponse). This suite is the complementary
// CLIENT-side oracle: it pins the datasource's pure request-shaping and error-handling contract by
// stubbing the global `fetch` and passing a fake `IGetRowsParams` with spy callbacks.
//
// It asserts:
//   (a) no quick filter               ⇒ request URL has NO `?q=` (R1.1)
//   (b) quick filter present          ⇒ request URL ends with `?q=<uri-encoded, trimmed>` (R1.2)
//   (c) 2xx response                  ⇒ successCallback(rowData, rowCount) is forwarded (R1.1)
//   (d) non-2xx response              ⇒ failCallback() called, onError invoked, success NOT called (R1.3)
//   (e) thrown/network fetch failure  ⇒ failCallback() called, onError invoked, success NOT called (R1.3)
//   (f) the POST body carries { startRow, endRow, sortModel, filterModel } (adapter bind contract)

import { afterEach, describe, expect, it, vi } from "vitest";

import {
  createVistaAgGridDatasource,
  type VistaAgGridDatasourceOptions,
} from "../src/vistaAgGridDatasource.js";

// --- Test helpers --------------------------------------------------------------------------------

const ENDPOINT = "/api/views/vProductCategory/aggrid";

// A minimal fake IGetRowsParams: only the fields the datasource reads/uses are populated. The real
// AG Grid type carries far more, so we build the subset and cast at the call site.
interface FakeParams {
  startRow: number;
  endRow: number;
  sortModel: unknown[];
  filterModel: Record<string, unknown>;
  successCallback: ReturnType<typeof vi.fn>;
  failCallback: ReturnType<typeof vi.fn>;
}

function makeParams(): FakeParams {
  return {
    startRow: 0,
    endRow: 100,
    sortModel: [{ colId: "CategoryName", sort: "asc" }],
    filterModel: { UnitPrice: { filterType: "number", type: "greaterThan", filter: 20 } },
    successCallback: vi.fn(),
    failCallback: vi.fn(),
  };
}

// Runs the datasource once with a stubbed global fetch, returning the captured request URL/init and
// the fake params (with its spy callbacks) for assertions.
async function runGetRows(
  options: VistaAgGridDatasourceOptions,
  fetchImpl: (url: string, init: RequestInit) => Promise<Response> | never,
): Promise<{ url: string; init: RequestInit; params: FakeParams }> {
  let capturedUrl = "";
  let capturedInit: RequestInit = {};

  const fetchSpy = vi.fn((input: unknown, init?: RequestInit) => {
    capturedUrl = String(input);
    capturedInit = init ?? {};
    return fetchImpl(capturedUrl, capturedInit);
  });
  vi.stubGlobal("fetch", fetchSpy);

  const datasource = createVistaAgGridDatasource(options);
  const params = makeParams();

  // The datasource's getRows returns a Promise<void>; await it so all callbacks have settled.
  await (datasource.getRows as (p: unknown) => Promise<void>)(params);

  return { url: capturedUrl, init: capturedInit, params };
}

// Builds a Response-like object good enough for the datasource (ok/status/statusText/json()).
function jsonResponse(body: unknown, init?: { ok?: boolean; status?: number; statusText?: string }): Response {
  const ok = init?.ok ?? true;
  const status = init?.status ?? 200;
  const statusText = init?.statusText ?? "OK";
  return {
    ok,
    status,
    statusText,
    json: async () => body,
  } as unknown as Response;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

// --- Tests ---------------------------------------------------------------------------------------

describe("createVistaAgGridDatasource — request shaping (R1.1, R1.2)", () => {
  it("(a) omits ?q= when no quick filter is supplied", async () => {
    const { url } = await runGetRows(
      { endpoint: ENDPOINT },
      async () => jsonResponse({ rowData: [], rowCount: 0 }),
    );

    expect(url).toBe(ENDPOINT);
    expect(url).not.toContain("?q=");
  });

  it("(a2) omits ?q= when the quick filter is whitespace-only (trimmed empty)", async () => {
    const { url } = await runGetRows(
      { endpoint: ENDPOINT, getQuickFilter: () => "   " },
      async () => jsonResponse({ rowData: [], rowCount: 0 }),
    );

    expect(url).toBe(ENDPOINT);
    expect(url).not.toContain("?q=");
  });

  it("(b) appends ?q=<uri-encoded, trimmed> when a quick filter is present", async () => {
    const { url } = await runGetRows(
      { endpoint: ENDPOINT, getQuickFilter: () => "  a & b  " },
      async () => jsonResponse({ rowData: [], rowCount: 0 }),
    );

    // Trimmed to "a & b", then URI-encoded.
    expect(url).toBe(`${ENDPOINT}?q=${encodeURIComponent("a & b")}`);
    expect(url.endsWith(`?q=${encodeURIComponent("a & b")}`)).toBe(true);
  });

  it("(b2) uses & as the separator when the endpoint already has a query string", async () => {
    const withQuery = `${ENDPOINT}?debug=1`;
    const { url } = await runGetRows(
      { endpoint: withQuery, getQuickFilter: () => "choc" },
      async () => jsonResponse({ rowData: [], rowCount: 0 }),
    );

    expect(url).toBe(`${withQuery}&q=choc`);
  });

  it("(f) POSTs a JSON body carrying { startRow, endRow, sortModel, filterModel }", async () => {
    const { init, params } = await runGetRows(
      { endpoint: ENDPOINT },
      async () => jsonResponse({ rowData: [], rowCount: 0 }),
    );

    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>)["Content-Type"]).toBe("application/json");

    const body = JSON.parse(String(init.body));
    expect(body).toEqual({
      startRow: params.startRow,
      endRow: params.endRow,
      sortModel: params.sortModel,
      filterModel: params.filterModel,
    });
  });
});

describe("createVistaAgGridDatasource — success/error handling (R1.1, R1.3)", () => {
  it("(c) forwards successCallback(rowData, rowCount) on a 2xx response", async () => {
    const rowData = [{ ProductId: 1 }, { ProductId: 2 }];
    const rowCount = 36;

    const { params } = await runGetRows(
      { endpoint: ENDPOINT },
      async () => jsonResponse({ rowData, rowCount }),
    );

    expect(params.successCallback).toHaveBeenCalledTimes(1);
    expect(params.successCallback).toHaveBeenCalledWith(rowData, rowCount);
    expect(params.failCallback).not.toHaveBeenCalled();
  });

  it("(d) on a non-2xx response calls failCallback() + onError and NOT successCallback", async () => {
    const onError = vi.fn();

    const { params } = await runGetRows(
      { endpoint: ENDPOINT, onError },
      async () => jsonResponse({ type: "about:blank", title: "Bad Request" }, { ok: false, status: 400, statusText: "Bad Request" }),
    );

    expect(params.failCallback).toHaveBeenCalledTimes(1);
    expect(params.successCallback).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledTimes(1);
    // The message surfaces the HTTP status so the UI can show a visible error indication.
    expect(String(onError.mock.calls[0][0])).toContain("400");
  });

  it("(e) on a thrown fetch (network failure) calls failCallback() + onError and NOT successCallback", async () => {
    const onError = vi.fn();

    const { params } = await runGetRows(
      { endpoint: ENDPOINT, onError },
      () => {
        throw new Error("network down");
      },
    );

    expect(params.failCallback).toHaveBeenCalledTimes(1);
    expect(params.successCallback).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledTimes(1);
    expect(String(onError.mock.calls[0][0])).toContain("network down");
  });

  it("(e2) tolerates a missing onError callback on failure (no throw)", async () => {
    const { params } = await runGetRows(
      { endpoint: ENDPOINT },
      async () => jsonResponse({}, { ok: false, status: 500, statusText: "Server Error" }),
    );

    expect(params.failCallback).toHaveBeenCalledTimes(1);
    expect(params.successCallback).not.toHaveBeenCalled();
  });
});
