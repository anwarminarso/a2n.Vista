// Vista DataTables adapter demo — client wiring.
//
// The view lives at /api/views/vProductCategory. Two adapter endpoints are exercised:
//   * POST {route}/datatable    — the DataTables server-side request/response contract.
//   * GET  {route}/querybuilder — the jQuery-QueryBuilder metadata schema (fields + operators).
//
// Naming note: Vista serializes rows with the ASP.NET Core "web" defaults (camelCase), so row cells
// arrive as productName, unitPrice, ... . Field *names* the server matches for sort/filter are the
// view's metadata names (PascalCase: ProductName, ...), compared case-sensitively. We therefore bind
// each column with a camelCase `data` (for rendering) plus a PascalCase `name` (the server field), and
// rewrite the outgoing DataTables request so `columns[i][data]` carries the PascalCase name.

const VIEW_ROUTE = '/api/views/vProductCategory';

// Structured-filter (jsonQB) payload, refreshed when the user clicks "Apply filters".
let currentQbJson = null;

const statusEl = document.getElementById('status');

function showStatus(message, isError) {
  statusEl.textContent = message;
  statusEl.className = isError ? 'err' : 'ok';
}

function clearStatus() {
  statusEl.textContent = '';
  statusEl.className = '';
}

// Column definitions: camelCase `data` reads the (camelCased) row cell; `name` is the server field name.
const COLUMNS = [
  { data: 'productName', name: 'ProductName', title: 'Product' },
  {
    data: 'unitPrice', name: 'UnitPrice', title: 'Unit price', className: 'dt-right',
    render: (d) => (d == null ? '' : '$' + Number(d).toFixed(2)),
  },
  { data: 'unitsInStock', name: 'UnitsInStock', title: 'In stock', className: 'dt-right' },
  { data: 'discontinued', name: 'Discontinued', title: 'Discontinued', render: (d) => (d ? 'Yes' : 'No') },
  { data: 'categoryName', name: 'CategoryName', title: 'Category' },
  { data: 'supplierName', name: 'SupplierName', title: 'Supplier' },
];

// Builds the current externalFilter (scope) JSON from the category selector, or null when "All".
function currentExternalFilter() {
  const categoryId = document.getElementById('categoryScope').value;
  return categoryId ? JSON.stringify({ CategoryId: Number(categoryId) }) : null;
}

// Loads the QueryBuilder schema from the server and initializes the filter builder. Boolean fields are
// given explicit radio values so the standalone QueryBuilder can render them.
async function initQueryBuilder() {
  const res = await fetch(`${VIEW_ROUTE}/querybuilder`, { headers: { Accept: 'application/json' } });
  if (!res.ok) {
    throw new Error(`querybuilder schema request failed (HTTP ${res.status})`);
  }
  const schema = await res.json();
  const filters = (schema.queryBuilderOptions && schema.queryBuilderOptions.filters) || [];

  filters.forEach((f) => {
    if (f.type === 'boolean') {
      f.input = 'radio';
      f.values = { true: 'Yes', false: 'No' };
    }
  });

  if (filters.length === 0) {
    throw new Error('querybuilder schema returned no filterable fields');
  }

  $('#builder').queryBuilder({ filters });
}

// Reads the QueryBuilder rules; returns the jsonQB string, or null when the builder is empty/invalid.
function readQbJson() {
  const rules = $('#builder').queryBuilder('getRules', { skip_empty: true });
  if (!rules || !rules.rules || rules.rules.length === 0) {
    return null;
  }
  return JSON.stringify(rules);
}

$(async function () {
  // Surface DataTables/ajax errors in-page instead of the default alert.
  $.fn.dataTable.ext.errMode = 'none';

  try {
    await initQueryBuilder();
    clearStatus();
  } catch (err) {
    showStatus(`Could not load the filter builder: ${err.message}`, true);
  }

  const table = $('#grid').DataTable({
    serverSide: true,
    processing: true,
    searching: true,
    order: [[0, 'asc']],
    pageLength: 10,
    lengthMenu: [10, 25, 50],
    columns: COLUMNS,
    ajax: {
      url: `${VIEW_ROUTE}/datatable`,
      type: 'POST',
      data: (d) => {
        // Send the PascalCase server field name as columns[i][data] (used for sort + per-column filter).
        d.columns.forEach((c) => {
          if (c.name) {
            c.data = c.name;
          }
        });
        // Attach the structured-filter and scope channels when present.
        if (currentQbJson) {
          d.jsonQB = currentQbJson;
        }
        const ext = currentExternalFilter();
        if (ext) {
          d.externalFilter = ext;
        }
      },
    },
  });

  table.on('xhr.dt', (e, settings, json, xhr) => {
    if (xhr && xhr.status >= 200 && xhr.status < 300) {
      clearStatus();
    }
  });

  table.on('error.dt', (e, settings, techNote, message) => {
    showStatus(`Request failed: ${message}. Check the server console for the Problem Details response.`, true);
  });

  document.getElementById('applyBtn').addEventListener('click', () => {
    currentQbJson = readQbJson();
    table.ajax.reload();
  });

  document.getElementById('resetBtn').addEventListener('click', () => {
    currentQbJson = null;
    document.getElementById('categoryScope').value = '';
    $('#builder').queryBuilder('reset');
    $('#grid').DataTable().search('').ajax.reload();
  });

  // Re-query immediately when the scope selector changes (scope is a first-class channel).
  document.getElementById('categoryScope').addEventListener('change', () => table.ajax.reload());
});
