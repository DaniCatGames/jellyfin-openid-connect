const ssoConfigLinking = {
    pluginUniqueId: "3b621017-67a3-461e-a820-21622c591827",
    loadProviders: (view) => {
        const provider_list = view.querySelector("#sso-provider-list");
        provider_list.innerHTML = "";

        ApiClient.getJSON(ApiClient.getUrl("OpenIDConnect/Providers/Names")).then((config_names) => {
            ssoConfigLinking.loadProviderList(provider_list, config_names);
        });
    },
    loadProviderList: (container, providers) => {
        providers.forEach((provider_name) => {
            const provider_config = document.createElement("div");
            // Add styling directly or via classes to make it look like a list item
            provider_config.style.background = "rgba(255,255,255,0.02)";
            provider_config.style.border = "1px solid var(--card-border, #333)";
            provider_config.style.borderRadius = "0.4em";
            provider_config.style.padding = "1em";
            provider_config.style.marginBottom = "1em";
            provider_config.setAttribute("data-id", provider_name);

            provider_config.innerHTML = `
                <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.5em;">
                    <h3 style="margin: 0; font-size: 1.2em;">${provider_name}</h3>
                    <a class="raised emby-button sso-provider-add-link" title="Link new account">
                        <span class="material-icons add" aria-hidden="true" style="margin-right: 0.2em;"></span>
                        Link Account
                    </a>
                </div>
                <div class="sso-provider-existing-links-container" data-provider="${provider_name}"></div>
            `;

            const add_provider = provider_config.querySelector(".sso-provider-add-link");
            add_provider.href = ApiClient.getUrl(`/OpenIDConnect/start/${provider_name}?isLinking=true`);

            container.appendChild(provider_config);
        });

        const currentUserId = ApiClient.getCurrentUserId();

        if (currentUserId) {
            ApiClient.getJSON(ApiClient.getUrl(`OpenIDConnect/links/${currentUserId}`)).then((provider_map) => {
                Object.keys(provider_map).forEach((provider_name) => {
                    const provider_container = container.querySelector(
                        `.sso-provider-existing-links-container[data-provider="${provider_name}"]`,
                    );
                    if (provider_container) {
                        ssoConfigLinking.populateExistingLinks(
                            provider_container,
                            provider_name,
                            provider_map[provider_name],
                        );
                    }
                });
            });
        }
    },

    populateExistingLinks: (container, provider_name, subs) => {
        container.querySelectorAll(".sso-provider-link-checkbox-wrapper").forEach((e) => e.remove());

        if (subs.length > 0) {
            container.innerHTML += `<hr style="border: none; border-top: 1px solid var(--card-border, #333); margin: 1em 0;" />`;
        }

        const checkboxes = subs.map((sub) => {
            // Use standard Jellyfin checkbox container classes instead of inline flex
            const out = document.createElement("div");
            out.classList.add("checkboxContainer", "sso-provider-link-checkbox-wrapper");
            out.style.margin = "0.8em 0";

            out.innerHTML = `
                <label>
                    <input
                        is="emby-checkbox"
                        class="sso-link-checkbox"
                        data-sub="${sub}"
                        data-provider="${provider_name}"
                        type="checkbox"
                    />
                    <span title="Full ID: ${sub}">
                        Linked Identity: <code style="background: rgba(0,0,0,0.3); padding: 0.2em 0.4em; border-radius: 0.3em; margin-left: 0.5em;">${sub}</code>
                    </span>
                </label>
            `;

            out.querySelector("input").addEventListener("change", () => {
                const anyChecked = document.querySelectorAll(".sso-link-checkbox:checked").length > 0;
                document.querySelector("#btn-delete-selected-links").disabled = !anyChecked;
            });

            return out;
        });

        checkboxes.forEach((e) => {
            container.appendChild(e);
        });
    },

    handleDeleteButtonPressed: (evt, view) => {
        if (evt.target.disabled) return;

        const currentUserId = ApiClient.getCurrentUserId();
        if (!currentUserId) return;

        const delete_requests = [...view.querySelectorAll(".sso-link-checkbox")]
            .filter((checkbox_link) => {
                const sub = checkbox_link.getAttribute("data-sub");
                const provider_name = checkbox_link.getAttribute("data-provider");

                if (![sub, provider_name].every((e) => e)) {
                    return false;
                }

                return checkbox_link.checked;
            })
            .map((checked_link) => {
                const sub = checked_link.getAttribute("data-sub");
                const provider_name = checked_link.getAttribute("data-provider");

                return ApiClient.fetch({
                    type: "DELETE",
                    url: ApiClient.getUrl(`OpenIDConnect/Link/${provider_name}/${currentUserId}/${sub}`),
                });
            });

        Promise.all(delete_requests).then((values) => {
            console.log({ message: "Delete requests handled", values });
            window.location.reload();
        });
    },
};

export default function (view) {
    ssoConfigLinking.loadProviders(view);

    view.querySelector("#btn-delete-selected-links").addEventListener("click", (e) =>
        ssoConfigLinking.handleDeleteButtonPressed(e, view),
    );
}
