let dirty = false;

const oidcConfigurationPage = {
    pluginUniqueId: "3b621017-67a3-461e-a820-21622c591827",

    showDashboard: (page) => {
        page.querySelector("#oidc-editor-view").style.display = "none";
        page.querySelector("#oidc-dashboard-view").style.display = "block";
    },

    showEditor: (page) => {
        page.querySelector("#oidc-dashboard-view").style.display = "none";
        page.querySelector("#oidc-editor-view").style.display = "block";
    },

    loadConfiguration: (page) => {
        ApiClient.getPluginConfiguration(oidcConfigurationPage.pluginUniqueId).then((config) => {
            oidcConfigurationPage.renderProviderList(page, config.OidConfigs || {});
        });

        const folder_container = page.querySelector("#EnabledFolders");
        oidcConfigurationPage.populateFolders(folder_container);
    },

    renderProviderList: (page, providers) => {
        const list = page.querySelector("#oidc-provider-list");
        const providerNames = Object.keys(providers);

        list.querySelectorAll(".oidc-provider-list-item, .oidc-empty-state").forEach((e) => e.remove());

        if (providerNames.length === 0) {
            const empty = document.createElement("div");
            empty.className = "oidc-empty-state";
            empty.innerHTML = `
            <div class="material-icons">vpn_key</div>
            <p><strong>No providers configured yet.</strong></p>
            <p>Click "Add Provider" below to get started.</p>
        `;
            list.appendChild(empty);
            return;
        }

        providerNames.forEach((name) => {
            const provider = providers[name];
            const item = document.createElement("div");
            item.className = "oidc-provider-list-item";
            item.setAttribute("data-provider", name);

            const isEnabled = provider.Enabled !== false;
            const endpoint = provider.OidEndpoint || "";
            const iconColor = isEnabled ? "var(--emphasis, #00a4dc)" : "#666";

            item.innerHTML = `
            <span class="oidc-provider-list-icon material-icons" style="color: ${iconColor};">
                ${isEnabled ? "check_circle" : "remove_circle"}
            </span>
            <span class="oidc-provider-list-name">${name}</span>
            <span class="oidc-provider-list-endpoint" title="${endpoint}">
                ${endpoint || "No endpoint set"}
            </span>
            <span class="oidc-provider-list-status ${isEnabled ? "enabled" : "disabled"}">
                ${isEnabled ? "Enabled" : "Disabled"}
            </span>
            <button class="oidc-provider-list-delete" title="Delete ${name}" data-provider="${name}">
                <span class="material-icons" style="font-size: 1.1em;">close</span>
            </button>
        `;

            item.addEventListener("click", (e) => {
                if (e.target.closest(".oidc-provider-list-delete")) return;
                oidcConfigurationPage.openEditor(page, name);
            });

            item.querySelector(".oidc-provider-list-delete").addEventListener("click", (e) => {
                e.stopPropagation();
                oidcConfigurationPage.deleteProvider(page, name);
            });

            list.appendChild(item);
        });
    },

    openEditor: (page, providerName) => {
        oidcConfigurationPage.showEditor(page);

        const titleDisplay = page.querySelector("#oidc-editor-provider-name-display");
        const modeBadge = page.querySelector("#oidc-editor-mode-badge");

        if (providerName) {
            modeBadge.textContent = "EDIT";
            modeBadge.className = "oidc-mode-badge oidc-mode-edit";
            titleDisplay.textContent = providerName;
            oidcConfigurationPage.loadProvider(page, providerName);
        } else {
            modeBadge.textContent = "NEW";
            modeBadge.className = "oidc-mode-badge oidc-mode-new";
            titleDisplay.textContent = "New Provider";
            oidcConfigurationPage.clearForm(page);
        }
    },

    clearForm: (page) => {
        dirty = false;

        const form = page.querySelector("#oidc-new-oidc-provider");

        form.querySelectorAll(".oidc-text").forEach((input) => {
            input.value = "";
        });

        form.querySelectorAll(".oidc-line-list").forEach((textarea) => {
            textarea.value = "";
        });

        form.querySelectorAll(".oidc-toggle").forEach((checkbox) => {
            checkbox.checked = checkbox.id === "Enabled";
        });

        const folderContainer = page.querySelector("#EnabledFolders");
        folderContainer.querySelectorAll(".folder-checkbox").forEach((cb) => {
            cb.checked = false;
        });

        const roleContainer = page.querySelector("#FolderRoleMapping");
        roleContainer.querySelectorAll(".oidc-role-mapping-container").forEach((e) => e.remove());

        oidcConfigurationPage.updateLibraryAccessVisibility(page);
        oidcConfigurationPage.updateLiveTvVisibility(page);
        oidcConfigurationPage.updateUserAccessVisibility(page);
    },

    populateEnabledFolders: (folder_list, container) => {
        container.querySelectorAll(".folder-checkbox").forEach((e) => {
            e.checked = folder_list.includes(e.getAttribute("data-id"));
        });
    },

    serializeEnabledFolders: (container) => {
        return [...container.querySelectorAll(".folder-checkbox")]
            .filter((e) => e.checked)
            .map((e) => e.getAttribute("data-id"));
    },

    populateFolders: (container) => {
        return ApiClient.getJSON(ApiClient.getUrl("Library/MediaFolders", { IsHidden: false })).then((folders) => {
            oidcConfigurationPage._populateFolders(container, folders);
        });
    },

    _populateFolders: (container, folders) => {
        container.querySelectorAll(".emby-checkbox-label").forEach((e) => e.remove());

        const checkboxes = folders.Items.map((folder) => {
            const out = document.createElement("label");
            out.innerHTML = `
                <input
                    is="emby-checkbox"
                    class="folder-checkbox chkFolder"
                    data-id="${folder.Id}"
                    type="checkbox"
                />
                <span>${folder.Name}</span>
            `;
            return out;
        });

        checkboxes.forEach((e) => {
            container.appendChild(e);
        });
    },

    populateRoleMappings: (folder_role_mappings, container) => {
        container.querySelectorAll(".oidc-role-mapping-container").forEach((e) => e.remove());

        const mapping_elements = folder_role_mappings.map((mapping) => {
            const elem = document.createElement("div");
            elem.classList.add("oidc-role-mapping-container");
            elem.innerHTML = `
                <label class="inputLabel inputLabelUnfocused oidc-role-mapping-input-label">Role:</label>
                <div class="listItem">
                    <input
                        is="emby-input"
                        required=""
                        type="text"
                        class="listItemBody oidc-role-mapping-name"
                        placeholder="role_name"
                    />
                    <button
                        type="button"
                        is="paper-icon-button-light"
                        class="listItemButton oidc-remove-role-mapping"
                    >
                        <span class="material-icons remove_circle" aria-hidden="true"></span>
                    </button>
                </div>
                <div class="checkboxList paperList oidc-folder-list"></div>
            `;

            const checklist = elem.querySelector(".oidc-folder-list");
            const enabled_folders = mapping["Folders"];

            oidcConfigurationPage
                .populateFolders(checklist)
                .then(() => oidcConfigurationPage.populateEnabledFolders(enabled_folders, checklist));

            elem.querySelector(".oidc-role-mapping-name").value = mapping["Role"];
            elem.querySelector(".oidc-remove-role-mapping").addEventListener(
                "click",
                oidcConfigurationPage.handleRoleMappingRemove,
            );

            return elem;
        });

        mapping_elements.forEach((e) => container.appendChild(e));
    },

    serializeRoleMappings: (container) => {
        const out = [];
        [...container.querySelectorAll(".oidc-role-mapping-container")].forEach((elem) => {
            const role = elem.querySelector(".oidc-role-mapping-name").value;
            const checklist = elem.querySelector(".oidc-folder-list");
            out.push({
                Role: role,
                Folders: oidcConfigurationPage.serializeEnabledFolders(checklist),
            });
        });
        return out;
    },

    handleRoleMappingRemove: (evt) => {
        const targeted_mapping = evt.target.closest(".oidc-role-mapping-container");
        targeted_mapping.remove();
    },

    listArgumentsByType: (page) => {
        const toggle_class = ".oidc-toggle";
        const text_class = ".oidc-text";
        const text_list_class = ".oidc-line-list";
        const folder_list_fields = ["EnabledFolders"];
        const role_map_fields = ["FolderRoleMapping"];

        const oidc_form = page.querySelector("#oidc-new-oidc-provider");

        const text_fields = [...oidc_form.querySelectorAll(text_class)].map((e) => e.id);
        const text_list_fields = [...oidc_form.querySelectorAll(text_list_class)].map((e) => e.id);
        const check_fields = [...oidc_form.querySelectorAll(toggle_class)].map((e) => e.id);

        return { text_fields, text_list_fields, check_fields, folder_list_fields, role_map_fields };
    },

    fillTextList: (text_list, element) => {
        element.value = text_list.join("\r\n");
    },

    parseTextList: (element) => {
        return element.value
            .split("\n")
            .map((e) => e.trim())
            .filter((e) => e);
    },

    updateLibraryAccessVisibility: (page) => {
        const enableAllFolders = page.querySelector("#EnableAllFolders").checked;
        const enableFolderRoles = page.querySelector("#EnableFolderRoles").checked;

        const foldersContainer = page.querySelector("#Container-EnabledFolders");
        const rolesToggleContainer = page.querySelector("#Container-EnableFolderRoles");
        const roleMappingContainer = page.querySelector("#Container-FolderRoleMapping");

        if (enableAllFolders) {
            foldersContainer.style.display = "none";
            rolesToggleContainer.style.display = "none";
            roleMappingContainer.style.display = "none";
        } else {
            foldersContainer.style.display = "block";
            rolesToggleContainer.style.display = "block";
            roleMappingContainer.style.display = enableFolderRoles ? "block" : "none";
        }
    },

    updateLiveTvVisibility: (page) => {
        const enableLiveTv = page.querySelector("#EnableLiveTv").checked;
        const enableLiveTvMgmt = page.querySelector("#EnableLiveTvManagement").checked;
        const enableLiveTvRoles = page.querySelector("#EnableLiveTvRoles").checked;

        const rbacToggleContainer = page.querySelector("#Container-EnableLiveTvRoles");
        const liveTvRolesContainer = page.querySelector("#Container-LiveTvRoles");
        const liveTvMgmtRolesContainer = page.querySelector("#Container-LiveTvManagementRoles");

        if (enableLiveTv && enableLiveTvMgmt) {
            rbacToggleContainer.style.display = "none";
            liveTvRolesContainer.style.display = "none";
            liveTvMgmtRolesContainer.style.display = "none";
        } else {
            rbacToggleContainer.style.display = "block";

            if (enableLiveTvRoles) {
                liveTvRolesContainer.style.display = enableLiveTv ? "none" : "block";
                liveTvMgmtRolesContainer.style.display = enableLiveTvMgmt ? "none" : "block";
            } else {
                liveTvRolesContainer.style.display = "none";
                liveTvMgmtRolesContainer.style.display = "none";
            }
        }
    },

    updateUserAccessVisibility: (page) => {
        const enableAuth = page.querySelector("#EnableAuthorization").checked;

        const rolesContainer = page.querySelector("#Container-Roles");
        const adminRolesContainer = page.querySelector("#Container-AdminRoles");

        rolesContainer.style.display = enableAuth ? "block" : "none";
        adminRolesContainer.style.display = enableAuth ? "block" : "none";
    },

    loadProvider: (page, provider_name) => {
        ApiClient.getPluginConfiguration(oidcConfigurationPage.pluginUniqueId).then((config) => {
            const provider = config.OidConfigs[provider_name] || {};
            const form_elements = oidcConfigurationPage.listArgumentsByType(page);

            page.querySelector("#OidProviderName").value = provider_name;

            form_elements.text_fields.forEach((id) => {
                if (provider[id]) page.querySelector("#" + id).value = provider[id];
            });

            form_elements.text_list_fields.forEach((id) => {
                if (provider[id]) oidcConfigurationPage.fillTextList(provider[id], page.querySelector("#" + id));
            });

            form_elements.folder_list_fields.forEach((id) => {
                if (provider[id]) {
                    oidcConfigurationPage.populateEnabledFolders(provider[id], page.querySelector(`#${id}`));
                }
            });

            form_elements.check_fields.forEach((id) => {
                if (provider[id] !== undefined) page.querySelector("#" + id).checked = provider[id];
            });

            form_elements.role_map_fields.forEach((id) => {
                const elem = page.querySelector(`#${id}`);
                if (provider[id]) oidcConfigurationPage.populateRoleMappings(provider[id], elem);
            });

            oidcConfigurationPage.updateLiveTvVisibility(page);
            oidcConfigurationPage.updateLibraryAccessVisibility(page);
            oidcConfigurationPage.updateUserAccessVisibility(page);
        });
    },

    deleteProvider: (page, provider_name) => {
        if (!window.confirm(`Are you sure you want to delete "${provider_name}"? This cannot be undone.`)) {
            return;
        }

        ApiClient.getPluginConfiguration(oidcConfigurationPage.pluginUniqueId).then((config) => {
            if (!config.OidConfigs.hasOwnProperty(provider_name)) {
                return;
            }

            delete config.OidConfigs[provider_name];

            ApiClient.updatePluginConfiguration(oidcConfigurationPage.pluginUniqueId, config).then((result) => {
                Dashboard.processPluginConfigurationUpdateResult(result);
                oidcConfigurationPage.loadConfiguration(page);
                oidcConfigurationPage.showDashboard(page);
                Dashboard.alert(`Provider "${provider_name}" removed.`);
            });
        });
    },

    saveProvider: (page, provider_name) => {
        if (!provider_name || !provider_name.trim()) {
            Dashboard.alert("Please enter a provider name.");
            return;
        }

        dirty = false;

        const form_elements = oidcConfigurationPage.listArgumentsByType(page);

        ApiClient.getPluginConfiguration(oidcConfigurationPage.pluginUniqueId).then((config) => {
            let current_config = {};
            if (config.OidConfigs.hasOwnProperty(provider_name)) {
                current_config = config.OidConfigs[provider_name];
            }

            form_elements.text_fields.forEach((id) => {
                const value = page.querySelector("#" + id).value;
                current_config[id] = value || null;
            });

            form_elements.check_fields.forEach((id) => {
                current_config[id] = page.querySelector("#" + id).checked;
            });

            form_elements.text_list_fields.forEach((id) => {
                current_config[id] = oidcConfigurationPage.parseTextList(page.querySelector("#" + id));
            });

            form_elements.folder_list_fields.forEach((id) => {
                const elem = page.querySelector(`#${id}`);
                current_config[id] = oidcConfigurationPage.serializeEnabledFolders(elem);
            });

            form_elements.role_map_fields.forEach((id) => {
                const elem = page.querySelector(`#${id}`);
                current_config[id] = oidcConfigurationPage.serializeRoleMappings(elem);
            });

            config.OidConfigs[provider_name] = current_config;

            ApiClient.updatePluginConfiguration(oidcConfigurationPage.pluginUniqueId, config).then((result) => {
                Dashboard.processPluginConfigurationUpdateResult(result);
                oidcConfigurationPage.loadConfiguration(page);
                oidcConfigurationPage.showDashboard(page);
                Dashboard.alert(`Provider "${provider_name}" saved.`);
            });
        });
    },

    addTextAreaStyle: (view) => {
        const style = document.createElement("link");
        style.rel = "stylesheet";
        style.href = ApiClient.getUrl("web/configurationpage") + "?name=openid-connect.css";
        view.appendChild(style);
    },
};

export default function (view) {
    oidcConfigurationPage.addTextAreaStyle(view);
    oidcConfigurationPage.loadConfiguration(view);
    oidcConfigurationPage.showDashboard(view);

    view.querySelector("#oidc-new-oidc-provider").addEventListener("input", () => {
        dirty = true;
    });

    view.querySelector("#oidc-new-oidc-provider").addEventListener("change", () => {
        dirty = true;
    });

    view.querySelector("#oidc-add-provider").addEventListener("click", () => {
        oidcConfigurationPage.openEditor(view, null);
    });

    view.querySelector("#oidc-back-to-dashboard").addEventListener("click", () => {
        oidcConfigurationPage.showDashboard(view);
    });

    view.querySelector("#TestProvider").addEventListener("click", (e) => {
        e.preventDefault();
        const provider_name = view.querySelector("#OidProviderName").value.trim();

        if (!provider_name) return;

        if (dirty) {
            const confirmSave = window.confirm(
                "You have unsaved changes. The test will run against the last saved version on the server. Do you want to save your changes now?",
            );
            if (confirmSave) {
                view.querySelector("#SaveProvider").click();
                Dashboard.alert("Configuration saved. Please click 'Test Connection' again.");
            }

            return;
        }

        const testUrl = ApiClient.getUrl(`OpenIDConnect/start/${provider_name}?isTesting=true`);
        window.open(testUrl, "_blank");
    });

    view.querySelector("#SaveProvider").addEventListener("click", (e) => {
        const provider_name = view.querySelector("#OidProviderName").value.trim();
        oidcConfigurationPage.saveProvider(view, provider_name);
        e.preventDefault();
        return false;
    });

    view.querySelector("#DeleteProvider").addEventListener("click", (e) => {
        const provider_name = view.querySelector("#OidProviderName").value.trim();
        if (provider_name) {
            oidcConfigurationPage.deleteProvider(view, provider_name);
        }
        e.preventDefault();
        return false;
    });

    view.querySelector("#AddRoleMapping").addEventListener("click", (e) => {
        const container = view.querySelector("#FolderRoleMapping");
        const current_mappings = oidcConfigurationPage.serializeRoleMappings(container);
        current_mappings.push({ Role: "", Folders: [] });
        oidcConfigurationPage.populateRoleMappings(current_mappings, container);
    });

    const selfServiceLink = view.querySelector("#oidc-self-service-link");
    selfServiceLink.href = "/OpenIDConnectViews/link";
    selfServiceLink.addEventListener("click", (e) => {
        window.location.href = "/OpenIDConnectViews/link";
    });

    view.querySelector("#ToggleSecret").addEventListener("click", () => {
        const secretInput = view.querySelector("#OidSecret");
        const icon = view.querySelector("#ToggleSecret .material-icons");

        if (secretInput.type === "password") {
            secretInput.type = "text";
            icon.textContent = "visibility_off";
        } else {
            secretInput.type = "password";
            icon.textContent = "visibility";
        }
    });

    view.querySelector("#EnableAllFolders").addEventListener("change", () => {
        oidcConfigurationPage.updateLibraryAccessVisibility(view);
    });

    view.querySelector("#EnableFolderRoles").addEventListener("change", () => {
        oidcConfigurationPage.updateLibraryAccessVisibility(view);
    });

    view.querySelector("#EnableLiveTvRoles").addEventListener("change", () => {
        oidcConfigurationPage.updateLiveTvVisibility(view);
    });

    view.querySelector("#EnableLiveTv").addEventListener("change", () => {
        oidcConfigurationPage.updateLiveTvVisibility(view);
    });

    view.querySelector("#EnableLiveTvManagement").addEventListener("change", () => {
        oidcConfigurationPage.updateLiveTvVisibility(view);
    });

    view.querySelector("#EnableAuthorization").addEventListener("change", () => {
        oidcConfigurationPage.updateUserAccessVisibility(view);
    });
}
