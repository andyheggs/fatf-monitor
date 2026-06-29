const DATA_ENDPOINT = "/api/fatf";

const state = {
  data: null,
  rows: [],
  filter: "all",
  search: ""
};

const elements = {
  retrieveButton: document.querySelector("#retrieveButton"),
  dataStatus: document.querySelector("#dataStatus"),
  checkedAt: document.querySelector("#checkedAt"),
  totalCount: document.querySelector("#totalCount"),
  increasedCount: document.querySelector("#increasedCount"),
  actionCount: document.querySelector("#actionCount"),
  freshness: document.querySelector("#freshness"),
  freshnessNote: document.querySelector("#freshnessNote"),
  resultSummary: document.querySelector("#resultSummary"),
  jurisdictionRows: document.querySelector("#jurisdictionRows"),
  searchInput: document.querySelector("#searchInput"),
  copyButton: document.querySelector("#copyButton"),
  jsonButton: document.querySelector("#jsonButton"),
  csvButton: document.querySelector("#csvButton"),
  sourceLinks: document.querySelector("#sourceLinks"),
  toast: document.querySelector("#toast")
};

function initializeIcons() {
  if (window.lucide) {
    window.lucide.createIcons();
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function formatCheckedAt(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Date unavailable";
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "short"
  }).format(date);
}

function freshnessDetails(value) {
  const checked = new Date(value);
  if (Number.isNaN(checked.getTime())) {
    return { label: "Unknown", note: "No valid timestamp" };
  }

  const hours = Math.max(0, (Date.now() - checked.getTime()) / 3600000);
  if (hours < 1) {
    return { label: "Current", note: "Updated within the hour" };
  }
  if (hours < 24) {
    const wholeHours = Math.floor(hours);
    return {
      label: "Current",
      note: `${wholeHours} hour${wholeHours === 1 ? "" : "s"} old`
    };
  }

  const days = Math.floor(hours / 24);
  return {
    label: days <= 2 ? "Current" : "Review",
    note: `${days} day${days === 1 ? "" : "s"} old`
  };
}

function buildRows(data) {
  const increased = data.increasedMonitoring || {};
  const action = data.callForAction || {};

  return [
    ...(increased.jurisdictions || []).map((name) => ({
      name,
      key: "increased",
      classification: "Increased monitoring",
      sourceName: increased.name || "FATF increased monitoring publication",
      sourceUrl: increased.sourceUrl || ""
    })),
    ...(action.jurisdictions || []).map((name) => ({
      name,
      key: "action",
      classification: "Call for action",
      sourceName: action.name || "FATF call for action publication",
      sourceUrl: action.sourceUrl || ""
    }))
  ].sort((a, b) => a.name.localeCompare(b.name));
}

function renderRows() {
  const term = state.search.trim().toLocaleLowerCase();
  const visibleRows = state.rows.filter((row) => {
    const matchesFilter = state.filter === "all" || row.key === state.filter;
    const matchesSearch = !term || row.name.toLocaleLowerCase().includes(term);
    return matchesFilter && matchesSearch;
  });

  if (!visibleRows.length) {
    elements.jurisdictionRows.innerHTML = `
      <tr class="empty-row">
        <td colspan="3">
          <div class="empty-state">
            <i data-lucide="search-x" aria-hidden="true"></i>
            <strong>No jurisdictions found</strong>
            <span>Adjust the search or classification filter.</span>
          </div>
        </td>
      </tr>`;
    elements.resultSummary.textContent = `0 of ${state.rows.length} jurisdictions shown`;
    initializeIcons();
    return;
  }

  elements.jurisdictionRows.innerHTML = visibleRows.map((row) => `
    <tr>
      <td>${escapeHtml(row.name)}</td>
      <td>
        <span class="classification classification-${row.key}">
          ${escapeHtml(row.classification)}
        </span>
      </td>
      <td>
        <a class="source-link" href="${escapeHtml(row.sourceUrl)}" target="_blank" rel="noopener noreferrer">
          <span>${escapeHtml(row.sourceName)}</span>
          <i data-lucide="external-link" aria-hidden="true"></i>
        </a>
      </td>
    </tr>
  `).join("");

  elements.resultSummary.textContent = `${visibleRows.length} of ${state.rows.length} jurisdictions shown`;
  initializeIcons();
}

function renderSources(data) {
  const sources = [
    data.increasedMonitoring,
    data.callForAction
  ].filter((source) => source?.sourceUrl);

  elements.sourceLinks.innerHTML = sources.map((source) => `
    <a class="source-link" href="${escapeHtml(source.sourceUrl)}" target="_blank" rel="noopener noreferrer">
      <span>${escapeHtml(source.name)}</span>
      <i data-lucide="external-link" aria-hidden="true"></i>
    </a>
  `).join("");
  initializeIcons();
}

function renderData(data) {
  state.data = data;
  state.rows = buildRows(data);

  const freshness = freshnessDetails(data.checkedAt);
  elements.checkedAt.textContent = formatCheckedAt(data.checkedAt);
  elements.totalCount.textContent = data.totalJurisdictions ?? state.rows.length;
  elements.increasedCount.textContent = data.increasedMonitoring?.count ?? "--";
  elements.actionCount.textContent = data.callForAction?.count ?? "--";
  elements.freshness.textContent = freshness.label;
  elements.freshnessNote.textContent = freshness.note;
  elements.searchInput.disabled = false;
  elements.copyButton.disabled = false;
  elements.jsonButton.disabled = false;
  elements.csvButton.disabled = false;

  renderRows();
  renderSources(data);
}

function setLoading(isLoading) {
  elements.retrieveButton.disabled = isLoading;
  elements.retrieveButton.classList.toggle("loading", isLoading);
  elements.dataStatus.className = "status-pill";
  elements.dataStatus.innerHTML = `
    <span class="status-dot"></span>
    ${isLoading ? "Retrieving" : "Ready"}
  `;
}

function setStatus(label, type = "live") {
  elements.dataStatus.className = `status-pill ${type}`;
  elements.dataStatus.innerHTML = `<span class="status-dot"></span>${escapeHtml(label)}`;
}

function showToast(message, type = "") {
  elements.toast.textContent = message;
  elements.toast.className = `toast visible ${type}`.trim();
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => {
    elements.toast.classList.remove("visible");
  }, 3200);
}

async function retrieveLatest() {
  setLoading(true);

  try {
    const response = await fetch(`${DATA_ENDPOINT}?t=${Date.now()}`, {
      headers: { Accept: "application/json" },
      cache: "no-store"
    });
    if (!response.ok) {
      throw new Error(`The monitor returned HTTP ${response.status}.`);
    }

    const data = await response.json();
    if (!data.increasedMonitoring || !data.callForAction) {
      throw new Error("The monitor response did not contain both FATF lists.");
    }

    renderData(data);
    setStatus("Latest loaded");
    showToast(`Retrieved ${data.totalJurisdictions ?? state.rows.length} jurisdictions.`);
  } catch (error) {
    setStatus("Retrieval failed", "error");
    showToast(error.message || "Unable to retrieve the FATF dataset.", "error");
  } finally {
    elements.retrieveButton.disabled = false;
    elements.retrieveButton.classList.remove("loading");
  }
}

function downloadFile(content, filename, type) {
  const blob = new Blob([content], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function exportJson() {
  if (!state.data) {
    return;
  }
  downloadFile(
    `${JSON.stringify(state.data, null, 2)}\n`,
    "fatf-jurisdictions-latest.json",
    "application/json;charset=utf-8"
  );
  showToast("JSON download prepared.");
}

function csvCell(value) {
  return `"${String(value ?? "").replaceAll('"', '""')}"`;
}

function exportCsv() {
  if (!state.rows.length) {
    return;
  }

  const header = ["Jurisdiction", "FATF classification", "Source publication", "Source URL", "Dataset checked"];
  const rows = state.rows.map((row) => [
    row.name,
    row.classification,
    row.sourceName,
    row.sourceUrl,
    state.data.checkedAt
  ]);
  const csv = [header, ...rows].map((row) => row.map(csvCell).join(",")).join("\r\n");
  downloadFile(`\uFEFF${csv}\r\n`, "fatf-jurisdictions-latest.csv", "text/csv;charset=utf-8");
  showToast(`CSV download prepared with ${state.rows.length} jurisdictions.`);
}

async function copyJson() {
  if (!state.data) {
    return;
  }

  try {
    await navigator.clipboard.writeText(JSON.stringify(state.data, null, 2));
    showToast("JSON copied to clipboard.");
  } catch {
    showToast("Clipboard access is unavailable in this browser.", "error");
  }
}

elements.retrieveButton.addEventListener("click", retrieveLatest);
elements.searchInput.addEventListener("input", (event) => {
  state.search = event.target.value;
  renderRows();
});
document.querySelectorAll(".segment").forEach((button) => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".segment").forEach((segment) => {
      const isActive = segment === button;
      segment.classList.toggle("active", isActive);
      segment.setAttribute("aria-pressed", String(isActive));
    });
    state.filter = button.dataset.filter;
    renderRows();
  });
});
elements.jsonButton.addEventListener("click", exportJson);
elements.csvButton.addEventListener("click", exportCsv);
elements.copyButton.addEventListener("click", copyJson);

window.addEventListener("DOMContentLoaded", initializeIcons);
