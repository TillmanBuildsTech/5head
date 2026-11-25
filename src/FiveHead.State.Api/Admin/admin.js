(() => {
  "use strict";

  const DEFAULT_ROUTE = "#/attention";
  const PAGE_LIMIT = 200;
  const OTHER = "__other__";
  const FIELDS = [
    ["status", "Status"],
    ["owner", "Owner"],
    ["nextStep", "Next step"]
  ];
  const LIST_COLUMNS = [
    ["title", "Title"],
    ["status", "Status"],
    ["owner", "Owner"],
    ["nextStep", "Next step"],
    ["claim", "Claim"],
    ["version", "Version"],
    ["updated", "Updated"]
  ];

  const loginView = document.querySelector("#login");
  const loginForm = document.querySelector("#login-form");
  const tokenInput = document.querySelector("#token");
  const loginError = document.querySelector("#login-error");
  const appView = document.querySelector("#app");
  const main = document.querySelector("#main");
  const navLinks = document.querySelectorAll("[data-nav]");

  let signedIn = false;
  let flash = null;
  let focusMain = false;
  // Unsaved matter edits. Kept in memory only, so a session expiry does not lose them.
  let draft = null;
  let lastMattersHash = "#/matters";
  let listSort = { key: "updated", dir: "desc" };

  class SessionExpired extends Error {}

  function el(tag, props, kids) {
    const node = document.createElement(tag);
    if (props) {
      for (const [key, value] of Object.entries(props)) {
        if (value == null || value === false) continue;
        if (key === "class") node.className = value;
        else node.setAttribute(key, String(value));
      }
    }
    for (const kid of kids || []) {
      node.append(kid instanceof Node ? kid : document.createTextNode(kid == null ? "" : String(kid)));
    }
    return node;
  }

  function clear(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
  }

  function unique(values) {
    return [...new Set(values.filter((value) => value != null && value !== ""))];
  }

  function navigate(hash) {
    // Setting the same hash does not fire hashchange, so re-render directly.
    if (location.hash === hash) render();
    else location.hash = hash;
  }

  function parseRoute() {
    const raw = (location.hash || DEFAULT_ROUTE).replace(/^#/, "");
    const split = raw.indexOf("?");
    const path = (split === -1 ? raw : raw.slice(0, split)).replace(/^\/+|\/+$/g, "");
    const params = new URLSearchParams(split === -1 ? "" : raw.slice(split + 1));
    const parts = path.split("/").filter(Boolean);
    if (parts[0] === "matters" && parts[1]) {
      let id = "";
      try {
        id = decodeURIComponent(parts[1]);
      } catch {
        return { name: "attention", params };
      }
      return { name: "matter", id, params };
    }
    if (parts[0] === "matters") return { name: "matters", params };
    if (parts[0] === "overview") return { name: "overview", params };
    return { name: "attention", params };
  }

  function request(path, options = {}) {
    const headers = { Accept: "application/json" };
    if (options.body) headers["Content-Type"] = "application/json";
    return fetch(path, {
      method: options.method || "GET",
      headers,
      body: options.body,
      credentials: "same-origin",
      mode: "same-origin"
    });
  }

  async function readJson(response) {
    const text = await response.text();
    if (!text) return {};
    try {
      return JSON.parse(text);
    } catch {
      return { message: text };
    }
  }

  // Every API call goes through here so a 401 always becomes a sign-in prompt.
  async function api(path, options = {}) {
    const response = await request(path, options);
    const body = await readJson(response);
    if (response.status === 401) throw new SessionExpired();
    return { ok: response.ok, status: response.status, body };
  }

  function expectOk(result, fallback) {
    if (!result.ok) throw new Error((result.body && result.body.message) || fallback);
    return result.body;
  }

  function showLogin(message) {
    signedIn = false;
    appView.classList.add("hidden");
    loginView.classList.remove("hidden");
    loginError.textContent = message || "";
    loginError.classList.toggle("hidden", !message);
  }

  function expireSession() {
    showLogin("Your session expired. Sign in again. Unsaved edits are kept and will come back.");
  }

  function showApp() {
    loginView.classList.add("hidden");
    appView.classList.remove("hidden");
  }

  function markNav(name) {
    navLinks.forEach((link) => {
      if (link.getAttribute("data-nav") === name) link.setAttribute("aria-current", "page");
      else link.removeAttribute("aria-current");
    });
  }

  function utcStamp(date) {
    return date.toISOString().replace("T", " ").slice(0, 16) + " UTC";
  }

  function relative(ms) {
    const diff = ms - Date.now();
    const abs = Math.abs(diff);
    if (abs < 60000) return diff >= 0 ? "in under a minute" : "just now";
    let text;
    if (abs < 3600000) text = Math.round(abs / 60000) + "m";
    else if (abs < 86400000) text = Math.round(abs / 3600000) + "h";
    else text = Math.round(abs / 86400000) + "d";
    return diff >= 0 ? "in " + text : text + " ago";
  }

  // Relative time for scanning, with the exact UTC time on hover and in the datetime attribute.
  function timeEl(iso) {
    const date = iso ? new Date(iso) : null;
    if (!date || Number.isNaN(date.getTime())) return el("span", { class: "muted" }, ["—"]);
    return el("time", { datetime: date.toISOString(), title: utcStamp(date) }, [relative(date.getTime())]);
  }

  function statusClass(status) {
    const key = String(status || "").toLowerCase();
    if (key === "done" || key === "closed" || key === "cancelled" || key === "canceled") return "st-done";
    if (key === "blocked" || key === "failed" || key === "error") return "st-bad";
    if (key === "active" || key === "in_progress" || key === "doing") return "st-go";
    return "";
  }

  function pill(text, extra) {
    return el("span", { class: "pill " + (extra || statusClass(text)) }, [text || "—"]);
  }

  function banner(message, tone, extra) {
    const node = el("div", { class: tone === "bad" ? "banner bad" : tone === "ok" ? "banner ok" : "banner", role: "status" }, [message]);
    if (extra) node.append(extra);
    return node;
  }

  function inlineError(container, message) {
    container.querySelectorAll("[data-inline]").forEach((node) => node.remove());
    const node = banner(message, "bad");
    node.setAttribute("role", "alert");
    node.dataset.inline = "1";
    container.prepend(node);
  }

  function takeFlash() {
    const current = flash;
    flash = null;
    return current;
  }

  function th(label, attrs) {
    return el("th", Object.assign({ scope: "col" }, attrs || {}), [label]);
  }

  function td(content, cls) {
    return el("td", cls ? { class: cls } : null, [content]);
  }

  function tr(cells) {
    return el("tr", null, cells);
  }

  function dataTable(caption, headers, rows) {
    return el("div", { class: "table-wrap" }, [
      el("table", null, [
        el("caption", { class: "sr" }, [caption]),
        el("thead", null, [el("tr", null, headers)]),
        el("tbody", null, rows)
      ])
    ]);
  }

  function panel(title, note, body) {
    const section = el("section", { class: "card panel" }, [el("h2", null, [title])]);
    if (note) section.append(el("p", { class: "section-note muted" }, [note]));
    if (body) section.append(body);
    return section;
  }

  // Owners and statuses for suggestions. Statuses come from the full overview; owners from recent matters.
  async function loadCatalog() {
    const [overview, list] = await Promise.all([
      api("/admin/api/overview"),
      api("/admin/api/matters?limit=" + PAGE_LIMIT)
    ]);
    const stats = expectOk(overview, "Overview failed.");
    const rows = expectOk(list, "List failed.");
    return {
      statuses: unique((stats.byStatus || []).map((row) => row.status)).sort(),
      owners: unique((Array.isArray(rows) ? rows : []).map((row) => row.owner)).sort()
    };
  }

  function ownerList(catalog, extra) {
    return el("datalist", { id: "owner-options" }, unique([...catalog.owners, ...extra]).map((owner) => el("option", { value: owner })));
  }

  // ---------- Needs attention ----------

  async function viewAttention() {
    document.title = "Needs attention · 5head admin";
    const [overviewRes, blockedRes] = await Promise.all([
      api("/admin/api/overview"),
      api("/admin/api/matters?status=blocked&limit=" + PAGE_LIMIT)
    ]);
    const overview = expectOk(overviewRes, "Overview failed.");
    const blocked = expectOk(blockedRes, "Blocked list failed.");
    const stale = (overview.claims || []).filter((row) => !row.active);

    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));
    root.append(el("p", { class: "lede muted" }, ["What is stuck: stale claims and blocked matters. Open one to act on it."]));

    if (!stale.length && !blocked.length) {
      root.append(panel(null, null, el("p", { class: "empty" }, ["Nothing is stuck. No stale claims and no blocked matters."])));
      return root;
    }

    if (stale.length) {
      root.append(panel(
        "Stale claims (" + stale.length + ")",
        "The claim's time limit ran out, so an agent may have stopped. Check before you clear it.",
        dataTable("Stale claims",
          [th("Matter"), th("Owner"), th("Claimed by"), th("Expired"), th("")],
          stale.map((row) => tr([
            td(el("a", { href: "#/matters/" + row.id }, [row.title]), "title"),
            td(row.owner),
            td(row.claimedBy || "—"),
            td(timeEl(row.claimExpiresAt)),
            td(el("a", { href: "#/matters/" + row.id }, ["Review"]))
          ])))
      ));
    }

    if (blocked.length) {
      root.append(panel(
        "Blocked (" + blocked.length + ")",
        "Matters marked blocked. Read the next step before changing anything.",
        dataTable("Blocked matters",
          [th("Matter"), th("Owner"), th("Next step"), th("Updated"), th("")],
          blocked.map((row) => tr([
            td(el("a", { href: "#/matters/" + row.id }, [row.title]), "title"),
            td(row.owner),
            td(row.nextStep || "—"),
            td(timeEl(row.updatedAt)),
            td(el("a", { href: "#/matters/" + row.id }, ["Review"]))
          ])))
      ));
    }
    return root;
  }

  // ---------- Overview ----------

  function statLink(label, value, href, tone) {
    return el("a", { class: "card stat" + (tone ? " " + tone : ""), href }, [
      el("b", null, [value]),
      el("span", null, [label])
    ]);
  }

  async function viewOverview() {
    document.title = "Overview · 5head admin";
    const body = expectOk(await api("/admin/api/overview"), "Overview failed.");
    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));

    const stale = body.staleClaims || 0;
    root.append(el("div", { class: "stats" }, [
      statLink("Matters", body.total || 0, "#/matters"),
      statLink("Active claims", body.activeClaims || 0, "#/matters"),
      statLink("Stale claims", stale, "#/attention", stale ? "warn" : "")
    ]));

    const statusRows = body.byStatus || [];
    root.append(panel(
      "By status",
      "Select a status to see its matters.",
      statusRows.length
        ? dataTable("Matter counts by status",
          [th("Status"), th("Count")],
          statusRows.map((row) => tr([
            td(el("a", { href: "#/matters?status=" + encodeURIComponent(row.status) }, [pill(row.status)])),
            td(row.count)
          ])))
        : el("p", { class: "empty" }, ["No matters yet."])
    ));

    const claims = body.claims || [];
    root.append(panel(
      "Claims",
      "Active claims are inside their time limit. Stale claims need a look.",
      claims.length
        ? dataTable("Current claims",
          [th("Matter"), th("Owner"), th("Claimed by"), th("Expires"), th("State")],
          claims.map((row) => tr([
            td(el("a", { href: "#/matters/" + row.id }, [row.title]), "title"),
            td(row.owner),
            td(row.claimedBy),
            td(timeEl(row.claimExpiresAt)),
            td(pill(row.active ? "active" : "stale", row.active ? "st-go" : "st-warn"))
          ])))
        : el("p", { class: "empty" }, ["No claims right now."])
    ));
    return root;
  }

  // ---------- Matters list ----------

  function sortValue(row, key) {
    switch (key) {
      case "version": return Number(row.version) || 0;
      case "updated": return Date.parse(row.updatedAt) || 0;
      case "claim": return row.claimedBy || "";
      case "nextStep": return row.nextStep || "";
      default: return String(row[key] || "");
    }
  }

  function sortRows(rows) {
    const mult = listSort.dir === "asc" ? 1 : -1;
    return [...rows].sort((a, b) => {
      const x = sortValue(a, listSort.key);
      const y = sortValue(b, listSort.key);
      if (typeof x === "number" && typeof y === "number") return (x - y) * mult;
      return String(x).localeCompare(String(y)) * mult;
    });
  }

  function matterTable(rows, redraw) {
    const headers = LIST_COLUMNS.map(([key, label]) => {
      const active = listSort.key === key;
      const arrow = active ? (listSort.dir === "asc" ? " ↑" : " ↓") : "";
      const button = el("button", { type: "button", class: "sort" }, [label + arrow]);
      button.addEventListener("click", () => {
        if (active) {
          listSort = { key, dir: listSort.dir === "asc" ? "desc" : "asc" };
        } else {
          listSort = { key, dir: key === "updated" || key === "version" ? "desc" : "asc" };
        }
        redraw();
      });
      const ariaSort = active ? (listSort.dir === "asc" ? "ascending" : "descending") : "none";
      return el("th", { scope: "col", "aria-sort": ariaSort }, [button]);
    });
    return dataTable("Matters", headers, sortRows(rows).map((row) => tr([
      td(el("a", { href: "#/matters/" + row.id }, [row.title]), "title"),
      td(pill(row.status)),
      td(row.owner),
      td(row.nextStep || "—"),
      td(row.claimedBy
        ? el("span", null, [row.claimedBy, " ", pill(row.claimActive ? "active" : "stale", row.claimActive ? "st-go" : "st-warn")])
        : "—"),
      td(row.version),
      td(timeEl(row.updatedAt))
    ])));
  }

  function filterForm(filters, catalog) {
    const statuses = unique([...catalog.statuses, filters.status]).sort();
    const status = el("select", { name: "status" }, [
      el("option", { value: "" }, ["Any status"]),
      ...statuses.map((value) => el("option", { value }, [value]))
    ]);
    status.value = filters.status;
    const owner = el("input", { type: "text", name: "owner", value: filters.owner, list: "owner-options", spellcheck: "false" });
    const clearButton = el("button", { class: "ghost", type: "button" }, ["Clear"]);
    const form = el("form", { class: "filters" }, [
      el("label", null, ["Status", status]),
      el("label", null, ["Owner", owner]),
      el("button", { class: "primary", type: "submit" }, ["Apply"]),
      clearButton
    ]);
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const next = new URLSearchParams();
      if (status.value) next.set("status", status.value);
      if (owner.value.trim()) next.set("owner", owner.value.trim());
      const query = next.toString();
      navigate(query ? "#/matters?" + query : "#/matters");
    });
    clearButton.addEventListener("click", () => navigate("#/matters"));
    return form;
  }

  async function viewMatters(route) {
    document.title = "Matters · 5head admin";
    const filters = {
      status: route.params.get("status") || "",
      owner: route.params.get("owner") || ""
    };
    const query = new URLSearchParams({ limit: String(PAGE_LIMIT) });
    if (filters.status) query.set("status", filters.status);
    if (filters.owner) query.set("owner", filters.owner);
    const [catalog, listRes] = await Promise.all([loadCatalog(), api("/admin/api/matters?" + query)]);
    const rows = expectOk(listRes, "List failed.");
    const list = Array.isArray(rows) ? rows : [];

    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));
    root.append(filterForm(filters, catalog));
    root.append(ownerList(catalog, []));

    const filtered = Boolean(filters.status || filters.owner);
    const host = el("div");
    const redraw = () => {
      clear(host);
      if (!list.length) {
        host.append(el("p", { class: "empty" }, [
          filtered ? "No matters match these filters. " : "No matters yet.",
          filtered ? el("a", { href: "#/matters" }, ["Clear filters"]) : ""
        ]));
        return;
      }
      const note = list.length + (list.length === 1 ? " matter" : " matters")
        + (list.length >= PAGE_LIMIT ? ". Only the first " + PAGE_LIMIT + " match. Narrow the filters to see the rest." : "");
      host.append(el("p", { class: "section-note muted count" }, [note]));
      host.append(matterTable(list, redraw));
    };
    redraw();
    root.append(el("section", { class: "card panel" }, [host]));
    return root;
  }

  // ---------- One matter ----------

  function buildClaimCard(matter) {
    const card = el("section", { class: "card stack" }, [el("h2", null, ["Claim"])]);
    if (!matter.claimedBy) {
      card.append(el("p", { class: "muted" }, ["Nobody holds this matter."]));
      return card;
    }
    const active = matter.claimActive === true;
    card.append(el("p", null, [pill(active ? "active" : "stale", active ? "st-go" : "st-warn"), " ", matter.claimedBy]));
    card.append(el("p", { class: "muted" }, [active ? "Expires " : "Expired ", timeEl(matter.claimExpiresAt)]));

    const trigger = el("button", { type: "button", class: "danger", "aria-expanded": "false" }, [
      active ? "Force-release claim…" : "Clear stale claim…"
    ]);
    const cancel = el("button", { type: "button", class: "ghost" }, ["Cancel"]);
    const confirmButton = el("button", { type: "button", class: "danger" }, [active ? "Yes, release it" : "Yes, clear it"]);
    const confirm = el("div", { class: "confirm", hidden: "" }, [
      el("p", null, [active
        ? "Another agent still holds this claim. Releasing it can let two agents work on the same matter. The timeline records admin.force_release."
        : "This claim has expired. Clearing it records admin.force_release in the timeline."]),
      el("div", { class: "row-actions" }, [confirmButton, cancel])
    ]);

    trigger.addEventListener("click", () => {
      confirm.hidden = false;
      trigger.setAttribute("aria-expanded", "true");
      cancel.focus();
    });
    cancel.addEventListener("click", () => {
      confirm.hidden = true;
      trigger.setAttribute("aria-expanded", "false");
      trigger.focus();
    });
    confirmButton.addEventListener("click", async () => {
      confirmButton.disabled = true;
      try {
        const result = await api("/admin/api/matters/" + encodeURIComponent(matter.id) + "/force-release", { method: "POST" });
        if (!result.ok) {
          confirmButton.disabled = false;
          inlineError(card, (result.body && result.body.message) || "Could not clear the claim.");
          return;
        }
        flash = { message: "Cleared the claim. The timeline records it as admin.", tone: "ok" };
        await render();
      } catch (error) {
        if (error instanceof SessionExpired) {
          expireSession();
          return;
        }
        confirmButton.disabled = false;
        inlineError(card, error.message || "Could not clear the claim.");
      }
    });

    card.append(trigger, confirm);
    return card;
  }

  function readEdit(form) {
    const select = form.elements.status;
    const other = form.elements.statusOther;
    return {
      status: (select.value === OTHER ? other.value : select.value).trim(),
      owner: form.elements.owner.value.trim(),
      nextStep: form.elements.nextStep.value.trim()
    };
  }

  async function saveEdit(form, matterId, version, values) {
    const submit = form.querySelector("button[type=submit]");
    if (submit) submit.disabled = true;
    // Keep the edit before the request, so a session expiry mid-save does not lose it.
    draft = { id: matterId, version, values };
    try {
      const result = await api("/admin/api/matters/" + encodeURIComponent(matterId), {
        method: "PATCH",
        body: JSON.stringify({
          version,
          status: values.status,
          owner: values.owner,
          nextStep: values.nextStep
        })
      });
      if (result.status === 409 && result.body && result.body.matter) {
        showConflict(form, matterId, result.body.matter, values);
        return;
      }
      if (!result.ok) {
        inlineError(form, (result.body && result.body.message) || "Save failed.");
        return;
      }
      draft = null;
      flash = { message: "Saved as admin. Version is now " + result.body.version + ".", tone: "ok" };
      await render();
    } catch (error) {
      if (error instanceof SessionExpired) {
        expireSession();
        return;
      }
      inlineError(form, error.message || "Save failed.");
    } finally {
      if (submit) submit.disabled = false;
    }
  }

  // A 409 shows each differing field side by side. The operator picks which values to keep, then saves on the new version.
  function showConflict(form, matterId, current, mine) {
    form.querySelectorAll(".conflict").forEach((node) => node.remove());
    const cur = {
      status: current.status || "",
      owner: current.owner || "",
      nextStep: current.nextStep || ""
    };
    const differing = FIELDS.filter(([key]) => (mine[key] || "") !== cur[key]);

    const panelNode = el("section", { class: "conflict", role: "alert" }, [
      el("h3", null, ["Someone saved this matter first"]),
      el("p", null, ["It is now version " + current.version + ". Choose which of your values to keep."])
    ]);

    if (differing.length) {
      panelNode.append(dataTable("Conflicting fields",
        [th("Field"), th("Your edit"), th("Current"), th("Keep mine")],
        differing.map(([key, label]) => tr([
          td(label),
          td(mine[key] || "(empty)"),
          td(cur[key] || "(empty)"),
          el("td", null, [el("input", { type: "checkbox", name: "keep-" + key, checked: "checked", "aria-label": "Keep my " + label.toLowerCase() })])
        ]))));
    } else {
      panelNode.append(el("p", { class: "muted" }, ["Only the version changed. Your values still apply."]));
    }

    const apply = el("button", { type: "button", class: "primary" }, ["Save on version " + current.version]);
    const discard = el("button", { type: "button", class: "ghost" }, ["Discard my edits"]);
    apply.addEventListener("click", () => {
      const merged = { ...cur };
      for (const [key] of differing) {
        const keep = panelNode.querySelector('input[name="keep-' + key + '"]');
        merged[key] = keep && keep.checked ? mine[key] : cur[key];
      }
      saveEdit(form, matterId, current.version, merged);
    });
    discard.addEventListener("click", () => {
      draft = null;
      flash = { message: "Discarded your edits. Showing version " + current.version + ".", tone: "ok" };
      render();
    });
    panelNode.append(el("div", { class: "row-actions" }, [apply, discard]));
    form.querySelector(".row-actions").before(panelNode);
  }

  async function viewMatter(route) {
    const [matterRes, catalog] = await Promise.all([
      api("/admin/api/matters/" + encodeURIComponent(route.id)),
      loadCatalog()
    ]);
    const root = el("div");
    if (matterRes.status === 404) {
      document.title = "Not found · 5head admin";
      root.append(el("p", null, ["That matter does not exist. ", el("a", { href: lastMattersHash }, ["Back to matters"])]));
      return root;
    }
    const matter = expectOk(matterRes, "Matter failed.");
    document.title = matter.title + " · 5head admin";

    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));

    const restoring = draft && draft.id === matter.id ? draft : null;
    // When restoring, save against the version the edit was made on, so a newer save shows up as a conflict.
    const baseVersion = restoring ? restoring.version : matter.version;
    const initial = restoring
      ? restoring.values
      : { status: matter.status || "", owner: matter.owner || "", nextStep: matter.nextStep || "" };
    if (restoring) {
      const discardDraft = el("button", { type: "button", class: "ghost" }, ["Discard edits"]);
      discardDraft.addEventListener("click", () => {
        draft = null;
        render();
      });
      const text = restoring.version === matter.version
        ? "Restored your unsaved edits."
        : "Restored your unsaved edits. This matter changed while you were away, so saving will show the differences first.";
      root.append(banner(text, "ok", discardDraft));
    }

    root.append(el("div", { class: "matter-head" }, [
      el("p", { class: "meta" }, [el("a", { href: lastMattersHash }, ["← Matters"])]),
      el("h1", null, [matter.title]),
      el("p", { class: "meta" }, [
        matter.id + " · version " + matter.version + " · updated ",
        timeEl(matter.updatedAt),
        matter.vectorRef ? " · " + matter.vectorRef : ""
      ])
    ]));

    // Edit form
    const statusOptions = unique([...catalog.statuses, matter.status, initial.status]).sort();
    const knownStatus = statusOptions.includes(initial.status);
    const statusSelect = el("select", { name: "status" }, [
      ...statusOptions.map((value) => el("option", { value }, [value])),
      el("option", { value: OTHER }, ["New status…"])
    ]);
    statusSelect.value = knownStatus ? initial.status : OTHER;
    const statusOther = el("input", {
      type: "text",
      name: "statusOther",
      "aria-label": "New status name",
      spellcheck: "false",
      value: knownStatus ? "" : initial.status
    });
    statusOther.hidden = knownStatus;

    const form = el("form", { class: "card stack", novalidate: "novalidate" }, [
      el("h2", null, ["Edit"]),
      el("p", { class: "muted" }, ["Saved as admin. If someone else saved first, you choose what to keep."]),
      el("label", null, ["Status", statusSelect, statusOther]),
      el("label", null, ["Owner", el("input", { type: "text", name: "owner", value: initial.owner, list: "owner-options", spellcheck: "false", required: "required" })]),
      el("label", null, ["Next step", el("input", { type: "text", name: "nextStep", value: initial.nextStep, spellcheck: "false" })]),
      ownerList(catalog, [matter.owner, initial.owner]),
      el("div", { class: "row-actions" }, [
        el("button", { class: "primary", type: "submit" }, ["Save"]),
        el("span", { class: "muted" }, ["Version " + baseVersion + " · recorded as admin"])
      ])
    ]);

    statusSelect.addEventListener("change", () => {
      statusOther.hidden = statusSelect.value !== OTHER;
      if (!statusOther.hidden) statusOther.focus();
    });
    const remember = () => {
      draft = { id: matter.id, version: baseVersion, values: readEdit(form) };
    };
    form.addEventListener("input", remember);
    form.addEventListener("change", remember);
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const values = readEdit(form);
      if (!values.status) {
        inlineError(form, "Pick or enter a status.");
        return;
      }
      if (!values.owner) {
        inlineError(form, "Owner is required.");
        return;
      }
      saveEdit(form, matter.id, baseVersion, values);
    });

    const timeline = panel("Timeline", "Newest first. Admin edits and force-releases are recorded as agent admin.");
    const events = [...(matter.events || [])].reverse();
    if (!events.length) {
      timeline.append(el("p", { class: "empty" }, ["No events yet."]));
    } else {
      const list = el("ol", { class: "timeline" });
      for (const item of events) {
        const kindClass = String(item.kind || "").startsWith("admin.") ? "st-admin" : "";
        const hasPayload = item.payload && typeof item.payload === "object" && Object.keys(item.payload).length > 0;
        const body = el("div", null, [
          el("div", { class: "event-top" }, [
            pill(item.kind, kindClass),
            el("span", null, [item.agent]),
            timeEl(item.createdAt)
          ]),
          el("p", { class: "summary" }, [item.summary]),
          hasPayload
            ? el("details", { class: "fold" }, [
              el("summary", null, ["Details"]),
              el("pre", { class: "payload" }, [JSON.stringify(item.payload, null, 2)])
            ])
            : ""
        ]);
        list.append(el("li", null, [el("span", { class: "dot", "aria-hidden": "true" }), body]));
      }
      timeline.append(list);
    }

    const metadata = panel("Metadata", null, el("details", { class: "fold" }, [
      el("summary", null, ["Show metadata JSON"]),
      el("pre", { class: "meta" }, [JSON.stringify(matter.metadata || {}, null, 2)])
    ]));

    const side = el("div", { class: "side" }, [buildClaimCard(matter), form]);
    root.append(el("div", { class: "layout" }, [side, el("div", null, [timeline, metadata])]));
    return root;
  }

  // ---------- Router ----------

  const VIEWS = {
    attention: viewAttention,
    overview: viewOverview,
    matters: viewMatters,
    matter: viewMatter
  };

  async function render() {
    if (!signedIn) return;
    const route = parseRoute();
    if (route.name === "matters") lastMattersHash = location.hash || "#/matters";
    markNav(route.name === "matter" ? "matters" : route.name);
    main.setAttribute("aria-busy", "true");
    clear(main);
    main.append(el("p", { class: "muted", role: "status" }, ["Loading shared state…"]));

    try {
      const view = await VIEWS[route.name](route);
      clear(main);
      main.append(view);
      if (focusMain) {
        focusMain = false;
        const heading = main.querySelector("h1");
        if (heading) heading.setAttribute("tabindex", "-1");
        (heading || main).focus();
      }
    } catch (error) {
      if (error instanceof SessionExpired) {
        expireSession();
        return;
      }
      clear(main);
      let message = (error && error.message) || "Something went wrong.";
      if (error instanceof TypeError) message = "The console could not reach the API.";
      main.append(banner(message, "bad"));
    } finally {
      main.removeAttribute("aria-busy");
    }
  }

  // ---------- Session ----------

  loginForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const token = tokenInput.value;
    if (!token.trim()) {
      showLogin("Enter the admin token.");
      tokenInput.focus();
      return;
    }
    let response;
    try {
      response = await request("/admin/session", {
        method: "POST",
        body: JSON.stringify({ token })
      });
    } catch {
      showLogin("The console could not reach the API.");
      return;
    }
    if (!response.ok) {
      showLogin("That token was rejected. Check it and try again.");
      tokenInput.select();
      tokenInput.focus();
      return;
    }
    tokenInput.value = "";
    signedIn = true;
    showApp();
    focusMain = true;
    if (!location.hash || location.hash === "#") location.hash = DEFAULT_ROUTE;
    await render();
  });

  document.querySelector("#logout").addEventListener("click", async () => {
    draft = null;
    flash = null;
    lastMattersHash = "#/matters";
    try {
      await request("/admin/session", { method: "DELETE" });
    } catch {
      // Clear the page even if the request fails.
    }
    showLogin();
    document.title = "5head admin";
  });

  window.addEventListener("hashchange", () => {
    focusMain = true;
    render();
  });

  request("/admin/api/overview")
    .then(async (response) => {
      if (response.status === 401) {
        showLogin();
        return;
      }
      if (!response.ok) {
        showLogin("The console could not reach the API.");
        return;
      }
      signedIn = true;
      showApp();
      if (!location.hash) location.hash = DEFAULT_ROUTE;
      await render();
    })
    .catch(() => showLogin("The console could not reach the API."));
})();
