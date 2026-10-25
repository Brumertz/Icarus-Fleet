/* ============================================================
   Icarus Fleet — dashboard logic (vanilla JS, no dependencies).
   Couples to index.html through exact IDs; every lookup is
   defensive so a missing element degrades instead of crashing.
   ============================================================ */

/* ---------- Module 1: element lookup ---------------------- */

/** Returns the element for an ID, warning (never throwing) when absent. */
function $(id) {
  const el = document.getElementById(id);
  if (!el) console.warn(`[icarus] missing element #${id}`);
  return el;
}

const els = {
  apiStatus: $("api-status"),
  apiStatusText: $("api-status-text"),
  refreshBtn: $("refresh-btn"),
  form: $("service-form"),
  clientName: $("client-name"),
  droneModel: $("drone-model"),
  problem: $("problem"),
  cost: $("cost"),
  priorityExpress: $("priority-express"),
  costPreview: $("cost-preview"),
  formError: $("form-error"),
  submitBtn: $("submit-btn"),
  nextTag: $("next-tag"),
  lastUpdated: $("last-updated"),
  toast: $("toast"),
  lists: {
    Regular: { list: $("regular-list"), count: $("regular-count"), empty: $("empty-regular") },
    Express: { list: $("express-list"), count: $("express-count"), empty: $("empty-express") },
    Finished: { list: $("finished-list"), count: $("finished-count"), empty: $("empty-finished") },
  },
};

/* ---------- Module 2: API client -------------------------- */

const EXPRESS_RATE = 0.15; // must match ServiceQueueManager.ExpressSurchargeRate
const BASE_URL = window.location.origin;

/**
 * Calls the API and parses JSON.
 * Rejects with the server-provided `{ error }` message when status != 2xx.
 */
async function api(path, options = {}) {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options,
  });

  if (res.status === 204) return null;

  const text = await res.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    /* non-JSON payload: keep body null */
  }

  if (!res.ok) {
    throw new Error(body?.error || `La API respondió ${res.status}`);
  }
  return body;
}

/* ---------- Module 3: rendering --------------------------- */

/** Formats an ISO timestamp as a local, short date-time. */
function formatTimestamp(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleString(undefined, {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** Money is always rendered with exactly two decimals. */
function formatMoney(value) {
  const n = Number(value);
  return Number.isFinite(n) ? n.toFixed(2) : "0.00";
}

/**
 * Builds one job row. Text is inserted through textContent only,
 * so client-provided strings can never inject markup.
 */
function buildJobRow(job, kind) {
  const li = document.createElement("li");
  li.className = "job";

  const top = document.createElement("div");
  top.className = "job__top";

  const tag = document.createElement("span");
  tag.className = "job__tag";
  tag.textContent = `#${job.serviceTag}`;

  const identity = document.createElement("div");
  const client = document.createElement("span");
  client.className = "job__client";
  client.textContent = job.clientName || "—";
  const model = document.createElement("span");
  model.className = "job__model";
  model.textContent = job.droneModel ? ` · ${job.droneModel}` : "";
  identity.append(client, model);

  const badge = document.createElement("span");
  badge.className = `badge${job.priority === "Express" ? " badge--express" : ""}`;
  badge.textContent = job.priority || "Regular";

  top.append(tag, identity, badge);

  const problem = document.createElement("p");
  problem.className = "job__problem";
  problem.textContent = job.problem || "";

  const foot = document.createElement("div");
  foot.className = "job__foot";

  const cost = document.createElement("span");
  cost.className = "job__cost";
  cost.textContent = `$${formatMoney(job.cost)}`;

  const when = document.createElement("span");
  when.className = "job__when";
  when.textContent = kind === "Finished"
    ? `procesado ${formatTimestamp(job.processedAt)}`
    : `creado ${formatTimestamp(job.createdAt)}`;

  foot.append(cost, when);

  // Row action: process queued jobs, delete finished ones.
  if (kind === "Finished") {
    const del = document.createElement("button");
    del.type = "button";
    del.className = "btn btn--sm btn--delete";
    del.dataset.action = "delete";
    del.dataset.tag = String(job.serviceTag);
    del.textContent = "Eliminar";
    foot.append(del);
  } else {
    const proc = document.createElement("button");
    proc.type = "button";
    proc.className = "btn btn--sm btn--process";
    proc.dataset.action = "process";
    proc.dataset.tag = String(job.serviceTag);
    proc.textContent = "Procesar";
    foot.append(proc);
  }

  li.append(top, problem, foot);
  return li;
}

/** Renders a full snapshot into the three columns. */
function render(snapshot) {
  const groups = {
    Regular: snapshot.regular || [],
    Express: snapshot.express || [],
    Finished: snapshot.finished || [],
  };

  for (const [kind, jobs] of Object.entries(groups)) {
    const target = els.lists[kind];
    if (!target?.list) continue;

    target.list.replaceChildren(...jobs.map((job) => buildJobRow(job, kind)));
    if (target.count) target.count.textContent = String(jobs.length);
    if (target.empty) target.empty.hidden = jobs.length > 0;
  }

  if (els.nextTag) els.nextTag.textContent = String(snapshot.nextServiceTag ?? "—");
  if (els.lastUpdated) {
    els.lastUpdated.textContent = `Actualizado ${new Date().toLocaleTimeString()}`;
  }
}

/** Loads the snapshot and renders it; surfaces load errors as a toast. */
async function refresh({ silent = false } = {}) {
  try {
    render(await api("/api/services"));
  } catch (err) {
    if (!silent) toast(err.message, "error");
  }
}

/* ---------- Module 4: feedback ---------------------------- */

let toastTimer = null;

/** Shows a transient message; `state` is "ok" or "error". */
function toast(message, state = "ok") {
  if (!els.toast) return;
  els.toast.textContent = message;
  els.toast.dataset.state = state;
  els.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    els.toast.hidden = true;
  }, 3200);
}

/** Shows or clears the inline form error. */
function setFormError(message) {
  if (!els.formError) return;
  els.formError.textContent = message || "";
  els.formError.hidden = !message;
}

/** Marks an input as invalid (or clears it). */
function markInvalid(input, invalid) {
  if (!input) return;
  input.setAttribute("aria-invalid", invalid ? "true" : "false");
}

/* ---------- Module 5: job form ---------------------------- */

/** Computes the final cost: Express gets the 15% surcharge, rounded to 2 decimals. */
function finalCost() {
  const base = parseCost(els.cost?.value);
  if (base === null) return null;
  const express = els.priorityExpress?.checked === true;
  return Math.round((express ? base * (1 + EXPRESS_RATE) : base) * 100) / 100;
}

/** Parses a decimal string, returning null when it is not a valid amount. */
function parseCost(raw) {
  if (typeof raw !== "string" || raw.trim() === "") return null;
  if (!/^\d+(\.\d{0,2})?$/.test(raw.trim())) return null;
  const value = Number(raw.trim());
  return Number.isFinite(value) ? value : null;
}

/** Keeps the live cost preview in sync with cost + priority. */
function updatePreview() {
  if (!els.costPreview) return;
  const cost = finalCost();
  const strong = document.createElement("strong");
  strong.textContent = cost === null ? "—" : `$${cost.toFixed(2)}`;
  els.costPreview.replaceChildren(document.createTextNode("Costo final: "), strong);
}

/** Client-side validation mirroring the rules enforced by ServiceQueueManager. */
function validateForm() {
  const checks = [
    { input: els.clientName, ok: !!els.clientName?.value.trim(), message: "El cliente es obligatorio." },
    { input: els.droneModel, ok: !!els.droneModel?.value.trim(), message: "El modelo del dron es obligatorio." },
    { input: els.problem, ok: !!els.problem?.value.trim(), message: "El problema es obligatorio." },
    { input: els.cost, ok: parseCost(els.cost?.value) !== null, message: "El costo debe ser un número con hasta 2 decimales." },
  ];

  let firstError = null;
  for (const check of checks) {
    markInvalid(check.input, !check.ok);
    if (!check.ok && !firstError) firstError = check.message;
  }
  return firstError;
}

/** Submits the form to POST /api/services. */
async function submitJob(event) {
  event.preventDefault();

  const invalid = validateForm();
  if (invalid) {
    setFormError(invalid);
    return;
  }

  const payload = {
    clientName: els.clientName.value.trim(),
    droneModel: els.droneModel.value.trim(),
    problem: els.problem.value.trim(),
    cost: parseCost(els.cost.value),
    priority: els.priorityExpress?.checked ? "Express" : "Regular",
  };

  setFormError("");
  if (els.submitBtn) els.submitBtn.disabled = true;

  try {
    const job = await api("/api/services", {
      method: "POST",
      body: JSON.stringify(payload),
    });
    toast(`Servicio #${job.serviceTag} agregado a la cola ${job.priority}`, "ok");
    els.form?.reset();
    updatePreview();
    await refresh({ silent: true });
  } catch (err) {
    setFormError(err.message);
  } finally {
    if (els.submitBtn) els.submitBtn.disabled = false;
  }
}

/* ---------- Module 6: row actions ------------------------- */

/** Handles process / delete clicks bubbling from any list. */
async function handleListAction(event) {
  const btn = event.target.closest("button[data-action]");
  if (!btn) return;

  const tag = Number(btn.dataset.tag);
  const action = btn.dataset.action;
  btn.disabled = true;

  try {
    if (action === "process") {
      const job = await api(`/api/services/${tag}/process`, { method: "POST" });
      toast(`Servicio #${job.serviceTag} procesado`, "ok");
    } else if (action === "delete") {
      await api(`/api/services/finished/${tag}`, { method: "DELETE" });
      toast(`Servicio #${tag} eliminado`, "ok");
    }
    await refresh({ silent: true });
  } catch (err) {
    toast(err.message, "error");
    btn.disabled = false;
  }
}

/* ---------- Module 7: health + polling -------------------- */

/** Pings /health and updates the status badge. */
async function checkHealth() {
  try {
    const data = await api("/health");
    if (els.apiStatus) els.apiStatus.dataset.state = "ok";
    if (els.apiStatusText) els.apiStatusText.textContent = `API ${data?.status || "ok"}`;
  } catch {
    if (els.apiStatus) els.apiStatus.dataset.state = "down";
    if (els.apiStatusText) els.apiStatusText.textContent = "API caída";
  }
}

/* ---------- Module 8: wiring ------------------------------ */

function init() {
  els.form?.addEventListener("submit", submitJob);
  els.refreshBtn?.addEventListener("click", () => refresh());

  // Live preview reacts to cost typing and priority switching.
  els.cost?.addEventListener("input", updatePreview);
  els.cost?.addEventListener("input", () => markInvalid(els.cost, false));
  els.priorityExpress?.addEventListener("change", updatePreview);
  document
    .querySelectorAll('input[name="priority"]')
    .forEach((radio) => radio.addEventListener("change", updatePreview));

  // Process / delete buttons live inside the lists.
  document
    .querySelectorAll(".list")
    .forEach((list) => list.addEventListener("click", handleListAction));

  updatePreview();
  checkHealth();
  refresh();

  // Light polling keeps the queue fresh without a manual refresh.
  setInterval(() => {
    if (document.visibilityState === "visible") refresh({ silent: true });
  }, 5000);
  setInterval(checkHealth, 15000);
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", init);
} else {
  init();
}
