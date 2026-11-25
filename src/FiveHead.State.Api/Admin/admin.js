(() => {
  const loginView = document.querySelector("#login");
  const loginError = document.querySelector("#login-error");
  const appView = document.querySelector("#app");
  const main = document.querySelector("#main");
  const navLinks = document.querySelectorAll("[data-nav]");

  let signedIn = false;
  let flash = null;

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

  function parseRoute() {
    const raw = (location.hash || "#/overview").replace(/^#/, "");
    const split = raw.indexOf("?");
    const path = (split === -1 ? raw : raw.slice(0, split)).replace(/^\/+|\/+$/g, "");
    const params = new URLSearchParams(split === -1 ? "" : raw.slice(split + 1));
    const parts = path.split("/").filter(Boolean);
    if (parts[0] === "matters" && parts[1]) {
      return { name: "matter", id: decodeURIComponent(parts[1]), params };
    }
    if (parts[0] === "matters") return { name: "matters", params };
    return { name: "overview", params };
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

  function showLogin(message) {
    signedIn = false;
    appView.classList.add("hidden");
    loginView.classList.remove("hidden");
    if (message) {
      loginError.textContent = message;
      loginError.classList.remove("hidden");
    } else {
      loginError.textContent = "";
      loginError.classList.add("hidden");
    }
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

  function formatTime(iso) {
    if (!iso) return "—";
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return String(iso);
    return date.toISOString().replace("T", " ").slice(0, 16) + " UTC";
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
    const node = el("div", { class: tone === "bad" ? "banner bad" : "banner", role: "status" }, [message]);
    if (extra) node.append(extra);
    return node;
  }

  function takeFlash() {
    const current = flash;
    flash = null;
    return current;
  }

  async function ensureOk(response) {
    if (response.status === 401) {
      showLogin("Sign in again. The session is missing or expired.");
      return null;
    }
    return response;
  }

  function renderOverview(body) {
    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));
    const stale = body.staleClaims || 0;
    root.append(el("div", { class: "stats" }, [
      el("section", { class: "card stat" }, [el("b", null, [body.total || 0]), el("span", null, ["Matters"])]),
      el("section", { class: "card stat" }, [el("b", null, [body.activeClaims || 0]), el("span", null, ["Active claims"])]),
      el("section", { class: stale ? "card stat warn" : "card stat" }, [el("b", null, [stale]), el("span", null, ["Stale claims"])])
    ]));

    const statuses = el("section", { class: "card panel" }, [
      el("h2", null, ["By status"]),
      el("p", { class: "section-note muted" }, ["Counts of every shared matter."])
    ]);
    if (!body.byStatus || body.byStatus.length === 0) {
      statuses.append(el("p", { class: "empty" }, ["No matters yet."]));
    } else {
      const table = el("table", null, [
        el("caption", { class: "sr" }, ["Matter counts by status"]),
        el("thead", null, [el("tr", null, [el("th", null, ["Status"]), el("th", null, ["Count"])])])
      ]);
      const tbody = el("tbody");
      for (const row of body.byStatus) {
        tbody.append(el("tr", null, [
          el("td", null, [pill(row.status)]),
          el("td", null, [row.count])
        ]));
      }
      table.append(tbody);
      statuses.append(table);
    }
    root.append(statuses);

    const claims = el("section", { class: "card panel" }, [
      el("h2", null, ["Claims"]),
      el("p", { class: "section-note muted" }, ["Active claims are still inside their TTL. Stale claims are stuck until someone clears them."])
    ]);
    if (!body.claims || body.claims.length === 0) {
      claims.append(el("p", { class: "empty" }, ["No claims right now."]));
    } else {
      const table = el("table", null, [
        el("caption", { class: "sr" }, ["Current claims"]),
        el("thead", null, [el("tr", null, [
          el("th", null, ["Matter"]),
          el("th", null, ["Owner"]),
          el("th", null, ["Claimed by"]),
          el("th", null, ["Expires"]),
          el("th", null, ["State"])
        ])])
      ]);
      const tbody = el("tbody");
      for (const row of body.claims) {
        tbody.append(el("tr", null, [
          el("td", { class: "title" }, [el("a", { href: "#/matters/" + row.id }, [row.title])]),
          el("td", null, [row.owner]),
          el("td", null, [row.claimedBy]),
          el("td", null, [formatTime(row.claimExpiresAt)]),
          el("td", null, [pill(row.active ? "active" : "stale", row.active ? "st-go" : "st-warn")])
        ]));
      }
      table.append(tbody);
      claims.append(table);
    }
    root.append(claims);
    return root;
  }

  function renderMatters(rows, params) {
    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));
    const form = el("form", { class: "filters" }, [
      el("label", null, ["Status", el("input", { name: "status", type: "text", value: params.get("status") || "", spellcheck: "false" })]),
      el("label", null, ["Owner", el("input", { name: "owner", type: "text", value: params.get("owner") || "", spellcheck: "false" })]),
      el("button", { class: "primary", type: "submit" }, ["Apply"]),
      el("button", { class: "ghost", type: "button", id: "clear-filters" }, ["Clear"])
    ]);
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const data = new FormData(form);
      const next = new URLSearchParams();
      const status = String(data.get("status") || "").trim();
      const owner = String(data.get("owner") || "").trim();
      if (status) next.set("status", status);
      if (owner) next.set("owner", owner);
      const query = next.toString();
      const hash = query ? "#/matters?" + query : "#/matters";
      if (location.hash === hash) render();
      else location.hash = hash;
    });
    form.querySelector("#clear-filters").addEventListener("click", () => {
      if (location.hash === "#/matters" || location.hash === "#/matters?") render();
      else location.hash = "#/matters";
    });
    root.append(form);

    const panel = el("section", { class: "card panel" });
    if (!rows.length) {
      panel.append(el("p", { class: "empty" }, ["No matters match these filters."]));
    } else {
      const table = el("table", null, [
        el("caption", { class: "sr" }, ["Matters"]),
        el("thead", null, [el("tr", null, [
          el("th", null, ["Title"]),
          el("th", null, ["Status"]),
          el("th", null, ["Owner"]),
          el("th", null, ["Next step"]),
          el("th", null, ["Claim"]),
          el("th", null, ["Version"])
        ])])
      ]);
      const tbody = el("tbody");
      for (const row of rows) {
        const claim = row.claimedBy ? row.claimedBy : "—";
        tbody.append(el("tr", null, [
          el("td", { class: "title" }, [el("a", { href: "#/matters/" + row.id }, [row.title])]),
          el("td", null, [pill(row.status)]),
          el("td", null, [row.owner]),
          el("td", null, [row.nextStep || "—"]),
          el("td", null, [claim]),
          el("td", null, [row.version])
        ]));
      }
      table.append(tbody);
      panel.append(table);
    }
    root.append(panel);
    return root;
  }

  function showInline(anchor, message, tone, matter) {
    const existing = anchor.parentElement && anchor.parentElement.querySelector(".banner");
    if (existing) existing.remove();
    const extra = el("div");
    if (matter) {
      extra.append(el("p", null, [
        "Current row: status " + (matter.status || "—")
          + ", owner " + (matter.owner || "—")
          + ", next step " + (matter.nextStep || "(none)")
          + ", version " + matter.version + "."
      ]));
      const reload = el("button", { type: "button", class: "ghost" }, ["Discard edits and reload"]);
      reload.addEventListener("click", () => render());
      extra.append(reload);
    }
    const node = banner(message, tone, extra);
    node.setAttribute("role", "alert");
    anchor.before(node);
  }

  function renderMatter(matter) {
    const root = el("div");
    const notice = takeFlash();
    if (notice) root.append(banner(notice.message, notice.tone));

    root.append(el("div", { class: "matter-head" }, [
      el("p", { class: "meta" }, [el("a", { href: "#/matters" }, ["Matters"])]),
      el("h1", null, [matter.title]),
      el("p", { class: "meta" }, [
        matter.id + " · version " + matter.version + " · updated " + formatTime(matter.updatedAt)
          + (matter.vectorRef ? " · " + matter.vectorRef : "")
      ])
    ]));

    const side = el("div");
    const claimCard = el("section", { class: "card stack" }, [el("h2", null, ["Claim"])]);
    if (!matter.claimedBy) {
      claimCard.append(el("p", { class: "muted" }, ["Nobody holds this matter."]));
    } else {
      const active = matter.claimExpiresAt && Date.parse(matter.claimExpiresAt) > Date.now();
      claimCard.append(el("p", null, [pill(active ? "active" : "stale", active ? "st-go" : "st-warn"), " ", matter.claimedBy]));
      claimCard.append(el("p", { class: "muted" }, ["Expires " + formatTime(matter.claimExpiresAt)]));
      const release = el("button", { type: "button", class: "danger" }, [active ? "Force-release claim" : "Clear stale claim"]);
      release.addEventListener("click", async () => {
        if (release.dataset.armed !== "1") {
          release.dataset.armed = "1";
          release.textContent = "Confirm clear claim";
          return;
        }
        release.disabled = true;
        const response = await ensureOk(await request("/admin/api/matters/" + matter.id + "/force-release", { method: "POST" }));
        if (!response) return;
        const payload = await readJson(response);
        if (!response.ok) {
          release.disabled = false;
          release.dataset.armed = "";
          showInline(claimCard, payload.message || "Could not clear the claim.", "bad", payload.matter);
          return;
        }
        flash = { message: "Cleared the claim. The timeline records it as admin.", tone: "ok" };
        await render();
      });
      claimCard.append(release);
      claimCard.append(el("p", { class: "muted" }, ["This clears the claim even if another agent still holds it, and writes an admin.force_release event."]));
    }
    side.append(claimCard);

    const form = el("form", { class: "card stack" });
    form.append(el("h2", null, ["Edit"]));
    form.append(el("p", { class: "muted" }, ["Status and owner changes skip the owner rule here. The version is still checked. A conflict does not write."]));
    form.append(el("label", null, ["Status", el("input", { name: "status", type: "text", value: matter.status || "", required: "required", spellcheck: "false" })]));
    form.append(el("label", null, ["Owner", el("input", { name: "owner", type: "text", value: matter.owner || "", required: "required", spellcheck: "false" })]));
    form.append(el("label", null, ["Next step", el("input", { name: "nextStep", type: "text", value: matter.nextStep || "", spellcheck: "false" })]));
    form.append(el("input", { type: "hidden", name: "version", value: matter.version }));
    const actions = el("div", { class: "row-actions" }, [
      el("button", { class: "primary", type: "submit" }, ["Save"]),
      el("span", { class: "muted" }, ["Version " + matter.version + " · recorded as admin"])
    ]);
    form.append(actions);
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const submit = form.querySelector("button[type=submit]");
      submit.disabled = true;
      const data = new FormData(form);
      const response = await ensureOk(await request("/admin/api/matters/" + matter.id, {
        method: "PATCH",
        body: JSON.stringify({
          version: Number(data.get("version")),
          status: String(data.get("status") || ""),
          owner: String(data.get("owner") || ""),
          nextStep: String(data.get("nextStep") || "")
        })
      }));
      if (!response) return;
      const payload = await readJson(response);
      submit.disabled = false;
      if (response.status === 409) {
        showInline(form, payload.message || "Version conflict.", "bad", payload.matter);
        return;
      }
      if (!response.ok) {
        showInline(form, payload.message || "Save failed.", "bad");
        return;
      }
      flash = { message: "Saved as admin. Version is now " + (payload.version != null ? payload.version : "updated") + ".", tone: "ok" };
      await render();
    });
    side.append(form);

    const timeline = el("section", { class: "card panel" }, [
      el("h2", null, ["Timeline"]),
      el("p", { class: "section-note muted" }, ["Append-only. Admin edits and force-releases show up here as agent admin."])
    ]);
    const events = matter.events || [];
    if (!events.length) {
      timeline.append(el("p", { class: "empty" }, ["No events yet."]));
    } else {
      const list = el("ol", { class: "timeline" });
      for (const item of events) {
        const kindClass = String(item.kind || "").startsWith("admin.") ? "st-admin" : "";
        const body = el("div", null, [
          el("div", { class: "event-top" }, [
            pill(item.kind, kindClass),
            el("span", null, [item.agent]),
            el("time", { datetime: item.createdAt || "" }, [formatTime(item.createdAt)])
          ]),
          el("p", { class: "summary" }, [item.summary])
        ]);
        const payload = item.payload && typeof item.payload === "object" && Object.keys(item.payload).length
          ? JSON.stringify(item.payload, null, 2)
          : "";
        if (payload) body.append(el("pre", { class: "payload" }, [payload]));
        list.append(el("li", null, [el("span", { class: "dot", "aria-hidden": "true" }), body]));
      }
      timeline.append(list);
    }

    const meta = el("section", { class: "card panel" }, [
      el("h2", null, ["Metadata"]),
      el("pre", { class: "meta" }, [JSON.stringify(matter.metadata || {}, null, 2)])
    ]);

    const mainCol = el("div", null, [timeline, meta]);
    root.append(el("div", { class: "layout" }, [side, mainCol]));
    return root;
  }

  async function render() {
    if (!signedIn) return;
    const route = parseRoute();
    markNav(route.name === "matter" ? "matters" : route.name);
    main.setAttribute("aria-busy", "true");
    clear(main);
    main.append(el("p", { class: "muted" }, ["Loading shared state…"]));

    try {
      if (route.name === "overview") {
        document.title = "Overview · 5head admin";
        const response = await ensureOk(await request("/admin/api/overview"));
        if (!response) return;
        const body = await readJson(response);
        if (!response.ok) throw new Error(body.message || "Overview failed.");
        clear(main);
        main.append(renderOverview(body));
      } else if (route.name === "matters") {
        document.title = "Matters · 5head admin";
        const query = new URLSearchParams();
        if (route.params.get("status")) query.set("status", route.params.get("status"));
        if (route.params.get("owner")) query.set("owner", route.params.get("owner"));
        const response = await ensureOk(await request("/admin/api/matters" + (query.toString() ? "?" + query.toString() : "")));
        if (!response) return;
        const rows = await readJson(response);
        if (!response.ok) throw new Error((rows && rows.message) || "List failed.");
        clear(main);
        main.append(renderMatters(Array.isArray(rows) ? rows : [], route.params));
      } else {
        document.title = "Matter · 5head admin";
        const response = await ensureOk(await request("/admin/api/matters/" + encodeURIComponent(route.id)));
        if (!response) return;
        const body = await readJson(response);
        clear(main);
        if (response.status === 404) {
          main.append(el("p", null, ["That matter does not exist. ", el("a", { href: "#/matters" }, ["Back to matters"])]));
          return;
        }
        if (!response.ok) throw new Error(body.message || "Matter failed.");
        main.append(renderMatter(body));
      }
    } catch (error) {
      clear(main);
      main.append(banner(error && error.message ? error.message : "Something went wrong.", "bad"));
    } finally {
      main.removeAttribute("aria-busy");
    }
  }

  document.querySelector("#login-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const token = String(new FormData(form).get("token") || "");
    form.reset();
    loginError.classList.add("hidden");
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
      showLogin("That token was rejected.");
      return;
    }
    signedIn = true;
    showApp();
    if (!location.hash || location.hash === "#") location.hash = "#/overview";
    await render();
  });

  document.querySelector("#logout").addEventListener("click", async () => {
    await request("/admin/session", { method: "DELETE" });
    flash = null;
    showLogin();
    document.title = "5head admin";
  });

  window.addEventListener("hashchange", () => { render(); });

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
      if (!location.hash) location.hash = "#/overview";
      await render();
    })
    .catch(() => showLogin("The console could not reach the API."));
})();
