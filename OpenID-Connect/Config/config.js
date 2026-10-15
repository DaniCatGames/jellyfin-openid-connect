// ============ 1. Constants + helpers ============

const PLUGIN_ID = "3b621017-67a3-461e-a820-21622c591827";
const OIDC_AUTH_PROVIDER_TYPE = "Jellyfin.Plugin.OpenIDConnect.AuthProvider";
const FOLDER_LIST_FIELD = "EnabledFolders";
const ROLE_MAP_FIELD = "FolderRoleMapping";

// Module-level state persists across SPA navigations; reset on every activation.
let dirty = false;
let configPromise = null;
let foldersPromise = null;
let writeQueue = Promise.resolve();
let editingName = "";

const q = (view, selector) => view.querySelector(selector);

const folderEntries = (folders) => folders.Items.map((f) => ({ id: f.Id, name: f.Name }));

// ============ 2. Config access ============

function getConfig() {
    configPromise ||= ApiClient.getPluginConfiguration(PLUGIN_ID);
    return configPromise;
}

// All writes run through a serialized read-modify-write queue. The plugin
// configuration is replaced as a whole object server-side, so concurrent
// saves (e.g. two toggles flipped quickly) must not interleave, or one
// write silently reverts the other. `mutate` returns false to abort.
function updateConfig(mutate) {
    const request = writeQueue.then(async () => {
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID); // always fresh for writes
        if (mutate(config) === false) return null;
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        configPromise = null; // next read refetches
        return result;
    });
    writeQueue = request.catch(() => {}); // keep the queue alive after failures
    return request;
}

function getFolders() {
    foldersPromise ||= ApiClient.getJSON(ApiClient.getUrl("Library/MediaFolders", { IsHidden: false }));
    return foldersPromise;
}

// ============ 3. Visibility engine ============

const attrIds = (node, attr) => (node.getAttribute(attr) || "").trim().split(/\s+/).filter(Boolean);

function applyVisibility(view) {
    const checked = (id) => q(view, `#${CSS.escape(id)}`)?.checked;

    for (const node of view.querySelectorAll("[data-hide-if], [data-hide-unless]")) {
        const hide =
            (node.hasAttribute("data-hide-if") && attrIds(node, "data-hide-if").every(checked)) ||
            (node.hasAttribute("data-hide-unless") && attrIds(node, "data-hide-unless").some((id) => !checked(id)));
        node.style.display = hide ? "none" : "";
    }
}

function wireVisibility(view) {
    const triggers = new Set();
    for (const node of view.querySelectorAll("[data-hide-if], [data-hide-unless]")) {
        for (const attr of ["data-hide-if", "data-hide-unless"]) {
            if (node.hasAttribute(attr)) attrIds(node, attr).forEach((id) => triggers.add(id));
        }
    }
    for (const id of triggers) {
        q(view, `#${CSS.escape(id)}`)?.addEventListener("change", () => applyVisibility(view));
    }
    applyVisibility(view);
}

// ============ 4. Scope tag editor ============

const FIXED_SCOPES = ["openid", "profile"];

const ScopeManager = {
    tags: [],

    init(view) {
        this.container = q(view, "#ScopesTagUI");
        this.input = q(view, "#ScopesTagInput");

        this.input.addEventListener("keydown", (e) => {
            if (["Enter", " ", ","].includes(e.key)) {
                e.preventDefault();
                this.add(this.input.value);
            } else if (e.key === "Backspace" && this.input.value === "" && this.tags.length > 0) {
                this.tags.pop();
                this.render();
                dirty = true;
            }
        });

        this.container.addEventListener("click", (e) => {
            if (!e.target.closest(".oidc-tag-remove")) this.input.focus();
        });
    },

    add(value) {
        const tag = value.trim().toLowerCase();
        if (!tag || this.tags.includes(tag) || FIXED_SCOPES.includes(tag)) {
            this.input.value = "";
            return;
        }
        this.tags.push(tag);
        this.input.value = "";
        this.render();
        dirty = true;
    },

    remove(tag) {
        this.tags = this.tags.filter((t) => t !== tag);
        this.render();
        dirty = true;
    },

    render() {
        this.container.querySelectorAll(".oidc-tag:not(.oidc-tag-fixed)").forEach((n) => n.remove());
        for (const tag of this.tags) {
            const chip = document.createElement("span");
            chip.className = "oidc-tag";
            chip.append(document.createTextNode(tag));

            const remove = document.createElement("span");
            remove.className = "oidc-tag-remove";
            remove.title = `Remove ${tag}`;
            remove.textContent = "×";
            remove.addEventListener("click", () => this.remove(tag));

            chip.append(remove);
            this.container.insertBefore(chip, this.input);
        }
    },

    get() {
        return this.tags;
    },

    set(tags) {
        this.tags = (tags || []).filter((t) => t && !FIXED_SCOPES.includes(t));
        this.render();
    },
};

// ============ 5. Rendering ============

function showDashboard(view) {
    q(view, "#oidc-editor-view").style.display = "none";
    q(view, "#oidc-dashboard-view").style.display = "block";
}

function showEditor(view) {
    q(view, "#oidc-dashboard-view").style.display = "none";
    q(view, "#oidc-editor-view").style.display = "block";
}

function renderProviderList(view, providers) {
    const list = q(view, "#oidc-provider-list");
    const names = Object.keys(providers);
    list.replaceChildren();
    q(view, "#oidc-empty-state").style.display = names.length ? "none" : "";

    for (const name of names) {
        const provider = providers[name];
        const enabled = provider.Enabled !== false;
        const endpoint = provider.Endpoint || "";

        const item = q(view, "#tpl-provider-item").content.firstElementChild.cloneNode(true);
        item.dataset.provider = name;

        const icon = item.querySelector(".oidc-provider-list-icon");
        icon.textContent = enabled ? "check_circle" : "remove_circle";
        icon.style.color = enabled ? "var(--emphasis, #00a4dc)" : "#666";

        item.querySelector(".oidc-provider-list-name").textContent = name;

        const endpointSpan = item.querySelector(".oidc-provider-list-endpoint");
        endpointSpan.textContent = endpoint || "No endpoint set";
        endpointSpan.title = endpoint;

        const status = item.querySelector(".oidc-provider-list-status");
        status.textContent = enabled ? "Enabled" : "Disabled";
        status.classList.add(enabled ? "enabled" : "disabled");

        item.querySelector(".oidc-provider-list-delete").title = `Delete ${name}`;
        list.append(item);
    }
}

// Generic checkbox list (libraries, users) cloned from #tpl-check-item.
function renderCheckList(view, container, entries, valueKey, selected = [], inputClass) {
    container.replaceChildren();
    for (const entry of entries) {
        const node = q(view, "#tpl-check-item").content.firstElementChild.cloneNode(true);
        const input = node.querySelector("input");
        input.className = inputClass;
        input.dataset[valueKey] = entry.id;
        input.checked = selected.includes(entry.id);
        node.querySelector("span").textContent = entry.name;
        container.append(node);
    }
}

function renderRoleMappings(view, container, mappings, folders) {
    container.replaceChildren();
    for (const mapping of mappings) {
        const node = q(view, "#tpl-role-mapping").content.firstElementChild.cloneNode(true);
        node.querySelector(".oidc-role-mapping-name").value = mapping.Role || "";
        renderCheckList(
            view,
            node.querySelector(".oidc-folder-list"),
            folderEntries(folders),
            "id",
            mapping.Folders || [],
            "folder-checkbox chkFolder",
        );
        container.append(node);
    }
}

async function populateUsers(view) {
    const users = await ApiClient.getUsers();
    renderCheckList(
        view,
        q(view, "#AutoLinkingAllowList"),
        users.map((u) => ({ id: u.Name, name: u.Name })),
        "username",
        [],
        "user-checkbox",
    );
}

async function populateAuthProviders(view) {
    const providers = await ApiClient.getJSON(ApiClient.getUrl("OpenIDConnect/AuthProviders"));
    const select = q(view, "#DefaultAuthProvider");
    select.replaceChildren();
    for (const provider of providers) {
        const option = document.createElement("option");
        option.value = provider.Type;
        option.textContent = provider.Name;
        select.append(option);
    }
}

function setAuthProviderValue(select, value) {
    const options = [...select.options];
    const match = options.find((o) => o.value === value);
    const fallback = options.find((o) => o.value === OIDC_AUTH_PROVIDER_TYPE) || options[0];
    select.value = (match || fallback)?.value ?? "";
}

function addStylesheet(view) {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = `${ApiClient.getUrl("web/configurationpage")}?name=openid-connect.css`;
    view.append(link);
}

// ============ 6. Load / save / delete / migration ============

// Form fields are discovered by class; every id doubles as the config key.
function listFields(view) {
    const form = q(view, "#oidc-provider-form");
    const idsOf = (selector) => [...form.querySelectorAll(selector)].map((e) => e.id);
    return {
        text: idsOf(".oidc-text"),
        textList: idsOf(".oidc-line-list"),
        check: idsOf(".oidc-toggle"),
        select: idsOf(".oidc-select"),
        folderList: [FOLDER_LIST_FIELD],
        roleMap: [ROLE_MAP_FIELD],
    };
}

function parseTextList(element) {
    return element.value
        .split("\n")
        .map((s) => s.trim())
        .filter(Boolean);
}

function serializeEnabledFolders(container) {
    return [...container.querySelectorAll(".folder-checkbox")].filter((cb) => cb.checked).map((cb) => cb.dataset.id);
}

function serializeRoleMappings(container) {
    return [...container.querySelectorAll(".oidc-role-mapping-container")].map((node) => ({
        Role: node.querySelector(".oidc-role-mapping-name").value,
        Folders: serializeEnabledFolders(node.querySelector(".oidc-folder-list")),
    }));
}

async function loadConfiguration(view) {
    try {
        const config = await getConfig();
        renderProviderList(view, config.Configs || {});
        q(view, "#GlobalAllowPrivateAvatarIp").checked = !!config.AllowPrivateAvatarIp;
        q(view, "#GlobalDisableAvatarSsrfCheck").checked = !!config.DisableAvatarSsrfCheck;
        applyVisibility(view); // private-IP field depends on the SSRF toggle
    } catch {
        Dashboard.alert("Failed to load plugin configuration.");
    }

    try {
        const folders = await getFolders();
        renderCheckList(
            view,
            q(view, `#${FOLDER_LIST_FIELD}`),
            folderEntries(folders),
            "id",
            [],
            "folder-checkbox chkFolder",
        );
    } catch {
        Dashboard.alert("Failed to load library list.");
    }

    populateUsers(view).catch(() => Dashboard.alert("Failed to load user list."));
    checkMigrationAvailable(view);
}

async function loadProvider(view, name) {
    const config = await getConfig();
    const provider = config.Configs[name] || {};
    const fields = listFields(view);

    q(view, "#ProviderName").value = name;
    ScopeManager.set(provider.Scopes);

    // Always assign (with fallbacks) so server-side-cleared values clear the form too.
    for (const id of fields.text) q(view, `#${id}`).value = provider[id] ?? "";
    for (const id of fields.textList) q(view, `#${id}`).value = (provider[id] || []).join("\r\n");
    for (const id of fields.select) setAuthProviderValue(q(view, `#${id}`), provider[id]);
    for (const id of fields.check) {
        if (provider[id] !== undefined) q(view, `#${id}`).checked = !!provider[id];
    }

    const folders = await getFolders();
    for (const id of fields.folderList) {
        renderCheckList(
            view,
            q(view, `#${id}`),
            folderEntries(folders),
            "id",
            provider[id] || [],
            "folder-checkbox chkFolder",
        );
    }
    for (const id of fields.roleMap) {
        renderRoleMappings(view, q(view, `#${id}`), provider[id] || [], folders);
    }

    for (const cb of q(view, "#AutoLinkingAllowList").querySelectorAll(".user-checkbox")) {
        cb.checked = !!provider.AutoLinkingAllowList?.includes(cb.dataset.username);
    }

    applyVisibility(view);
    dirty = false;
}

function clearForm(view) {
    q(view, "#ProviderName").value = "";
    const form = q(view, "#oidc-provider-form");

    form.querySelectorAll(".oidc-text").forEach((input) => (input.value = ""));
    form.querySelectorAll(".oidc-line-list").forEach((textarea) => (textarea.value = ""));
    form.querySelectorAll(".oidc-toggle").forEach((cb) => (cb.checked = cb.id === "Enabled"));
    form.querySelectorAll(".oidc-select").forEach((select) => setAuthProviderValue(select, OIDC_AUTH_PROVIDER_TYPE));

    q(view, `#${FOLDER_LIST_FIELD}`)
        .querySelectorAll(".folder-checkbox")
        .forEach((cb) => (cb.checked = false));
    q(view, `#${ROLE_MAP_FIELD}`).replaceChildren();
    q(view, "#AutoLinkingAllowList")
        .querySelectorAll(".user-checkbox")
        .forEach((cb) => (cb.checked = false));

    ScopeManager.set([]);
    applyVisibility(view);
    dirty = false;
}

async function openEditor(view, name) {
    showEditor(view);
    await populateAuthProviders(view);

    editingName = name;

    const badge = q(view, "#oidc-editor-mode-badge");
    if (name) {
        badge.textContent = "EDIT";
        badge.className = "oidc-mode-badge oidc-mode-edit";
        q(view, "#oidc-editor-provider-name-display").textContent = name;
        await loadProvider(view, name);
    } else {
        badge.textContent = "NEW";
        badge.className = "oidc-mode-badge oidc-mode-new";
        q(view, "#oidc-editor-provider-name-display").textContent = "New Provider";
        clearForm(view);
    }
}

function saveGlobalSettings(view) {
    const allowPrivateIp = q(view, "#GlobalAllowPrivateAvatarIp").checked;
    const disableSsrf = q(view, "#GlobalDisableAvatarSsrfCheck").checked;

    updateConfig((config) => {
        config.AllowPrivateAvatarIp = allowPrivateIp;
        config.DisableAvatarSsrfCheck = disableSsrf;
    })
        .then((result) => Dashboard.processPluginConfigurationUpdateResult(result))
        .catch(() => Dashboard.alert("Failed to save global settings."));
}

async function saveProvider(view) {
    // Native constraint validation (required fields, provider-name pattern).
    if (!q(view, "#ProviderName").reportValidity()) return false;

    const name = q(view, "#ProviderName").value.trim();
    const fields = listFields(view);
    const provider = {};

    for (const id of fields.text) provider[id] = q(view, `#${id}`).value || null;
    for (const id of fields.check) provider[id] = q(view, `#${id}`).checked;
    for (const id of fields.textList) provider[id] = parseTextList(q(view, `#${id}`));
    for (const id of fields.select) provider[id] = q(view, `#${id}`).value || null;
    for (const id of fields.folderList) provider[id] = serializeEnabledFolders(q(view, `#${id}`));
    for (const id of fields.roleMap) provider[id] = serializeRoleMappings(q(view, `#${id}`));
    provider.AutoLinkingAllowList = [...q(view, "#AutoLinkingAllowList").querySelectorAll(".user-checkbox")]
        .filter((cb) => cb.checked)
        .map((cb) => cb.dataset.username);
    provider.Scopes = ScopeManager.get();

    try {
        // Merge over the existing entry so server-side-only fields survive.
        const result = await updateConfig((config) => {
            if (name !== editingName && Object.hasOwn(config.Configs, name)) {
                alert(`Provider "${name}" already exists.`);
                return false;
            }
            const merged = { ...(config.Configs[editingName] || {}), ...provider };
            if (name !== editingName) delete config.Configs[editingName];
            config.Configs[name] = merged;
        });
        Dashboard.processPluginConfigurationUpdateResult(result);
        dirty = false;
        loadConfiguration(view);
        showDashboard(view);
        return true;
    } catch {
        Dashboard.alert(`Failed to save provider "${name}".`);
        return false;
    }
}

async function deleteProvider(view, name) {
    if (!window.confirm(`Are you sure you want to delete "${name}"? This cannot be undone.`)) return;

    try {
        const result = await updateConfig((config) => {
            if (!Object.hasOwn(config.Configs, name)) return false;
            delete config.Configs[name];
            return true;
        });
        if (result === null) {
            Dashboard.alert(`Provider "${name}" no longer exists on the server.`);
            return;
        }
        Dashboard.processPluginConfigurationUpdateResult(result);
        Dashboard.alert(`Provider "${name}" removed.`);
        loadConfiguration(view);
        showDashboard(view);
    } catch {
        Dashboard.alert(`Failed to delete provider "${name}".`);
    }
}

async function checkMigrationAvailable(view) {
    const button = q(view, "#oidc-migrate-btn");
    try {
        const available = await ApiClient.getJSON(ApiClient.getUrl("OpenIDConnect/Providers/MigrationAvailable"));
        button.style.display = available ? "" : "none";
    } catch {
        button.style.display = "none";
    }
}

async function migrateProviders(view) {
    if (
        !window.confirm(
            "This will migrate settings from 9p4's SSO plugin. Current providers will be overridden if they have the same name. Linked users and the avatar URL will not be migrated. Continue?",
        )
    )
        return;

    try {
        await ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("OpenIDConnect/Providers/Migrate") });
        Dashboard.alert("Migration successful.");
    } catch {
        Dashboard.alert(
            "Migration failed — check the server logs. Either no 9p4 configuration was found or the request was rejected.",
        );
    }
    configPromise = null;
    loadConfiguration(view);
}

// ============ 7. Page controller ============

export default function (view) {
    // Reset module state for this activation.
    dirty = false;
    configPromise = null;
    foldersPromise = null;
    writeQueue = Promise.resolve();

    addStylesheet(view);
    ScopeManager.init(view);
    wireVisibility(view);
    loadConfiguration(view);
    showDashboard(view);

    // Provider list — event delegation survives re-renders.
    q(view, "#oidc-provider-list").addEventListener("click", (e) => {
        const item = e.target.closest(".oidc-provider-list-item");
        if (!item) return;
        if (e.target.closest(".oidc-provider-list-delete")) {
            deleteProvider(view, item.dataset.provider);
        } else {
            openEditor(view, item.dataset.provider);
        }
    });

    q(view, "#oidc-add-provider").addEventListener("click", () => openEditor(view, null));
    q(view, "#oidc-back-to-dashboard").addEventListener("click", () => showDashboard(view));
    q(view, "#oidc-migrate-btn").addEventListener("click", () => migrateProviders(view));

    const form = q(view, "#oidc-provider-form");
    form.addEventListener("submit", (e) => {
        e.preventDefault();
        saveProvider(view);
    });
    form.addEventListener("input", () => {
        dirty = true;
    });
    form.addEventListener("change", () => {
        dirty = true;
    });

    q(view, "#TestProvider").addEventListener("click", async (e) => {
        e.preventDefault();
        if (!form.reportValidity()) return;

        if (dirty) {
            const saveNow = window.confirm(
                "You have unsaved changes. The test runs against the version saved on the server. Save your changes now and start the test?",
            );
            if (!saveNow) return;
            if (!(await saveProvider(view))) return;
        }

        const name = q(view, "#ProviderName").value.trim();
        window.open(ApiClient.getUrl(`OpenIDConnect/start/${encodeURIComponent(name)}`, { isTesting: true }), "_blank");
    });

    q(view, "#DeleteProvider").addEventListener("click", (e) => {
        e.preventDefault();
        const name = q(view, "#ProviderName").value.trim();
        if (name) deleteProvider(view, name);
    });

    q(view, "#AddRoleMapping").addEventListener("click", async () => {
        const container = q(view, `#${ROLE_MAP_FIELD}`);
        const mappings = serializeRoleMappings(container);
        mappings.push({ Role: "", Folders: [] });
        renderRoleMappings(view, container, mappings, await getFolders());
        dirty = true;
    });

    // Remove role mapping — one delegated listener for all rows.
    q(view, `#${ROLE_MAP_FIELD}`).addEventListener("click", (e) => {
        const button = e.target.closest(".oidc-remove-role-mapping");
        if (!button) return;
        button.closest(".oidc-role-mapping-container")?.remove();
        dirty = true;
    });

    // Anchor navigates natively; point it at the API server so it also works
    // when the web UI is hosted on a different origin than the server.
    q(view, "#oidc-self-service-link").href = ApiClient.getUrl("OpenIDConnectViews/link");

    q(view, "#ToggleSecret").addEventListener("click", () => {
        const input = q(view, "#Secret");
        const icon = q(view, "#ToggleSecret .material-icons");
        const show = input.type === "password";
        input.type = show ? "text" : "password";
        icon.textContent = show ? "visibility_off" : "visibility";
    });

    q(view, "#GlobalAllowPrivateAvatarIp").addEventListener("change", () => saveGlobalSettings(view));
    q(view, "#GlobalDisableAvatarSsrfCheck").addEventListener("change", () => saveGlobalSettings(view));
}
