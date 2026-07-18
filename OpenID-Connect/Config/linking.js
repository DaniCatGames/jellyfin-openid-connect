const ssoConfigLinking = {
    pluginUniqueId: "3b621017-67a3-461e-a820-21622c591827",
    loadProviders: (view) => {
        const provider_list_id = "sso-provider-list";
        const provider_list_oid_id = `${provider_list_id}-oid`;

        const provider_list_oid = view.querySelector(`#${provider_list_oid_id}`);
        provider_list_oid.innerHTML = "";

        ApiClient.getJSON(ApiClient.getUrl("OpenIDConnect/GetNames")).then((config_names) => {
            ssoConfigLinking.loadProviderList(provider_list_oid, config_names);
        });
    },
    loadProviderList: (container, providers) => {
        providers.forEach((provider_name) => {
            var provider_config = document.createElement("div");
            provider_config.classList.add("sso-provider-links-container");
            provider_config.setAttribute("data-id", provider_name);

            provider_config.innerHTML = `
      <label
        class="inputLabel inputLabelUnfocused sso-provider-link-title"
      >${provider_name}
      </label>
      <a
        class="fab emby-button sso-provider-add-link"
      >
        <span class="material-icons add" aria-hidden="true"></span>
      </a>
      <div
        class="sso-provider-existing-links-container"
        data-provider="${provider_name}"
      ></div>
      `;
            var add_provider = provider_config.querySelector(".sso-provider-add-link");

            //const provider_name_css = ssoConfigLinking.safeCSSId(provider_name);
            //provider_link.id = "sso-provider-" + provider_name_css;
            //provider_link.classList.add("sso-provider-" + provider_name_css);
            add_provider.classList.add("sso-provider");

            add_provider.href = ApiClient.getUrl(`/OpenIDConnect/start/${provider_name}?isLinking=true`);

            container.appendChild(provider_config);
        });

        const currentUserId = ApiClient.getCurrentUserId();

        if (currentUserId) {
            ApiClient.getJSON(ApiClient.getUrl(`OpenIDConnect/links/${currentUserId}`)).then((provider_map) => {
                console.log({ provider_map, currentUserId });

                Object.keys(provider_map).forEach((provider_name) => {
                    const provider_container = container.querySelector(
                        `.sso-provider-existing-links-container[data-provider="${provider_name}"]`,
                    );
                    ssoConfigLinking.populateExistingLinks(
                        provider_container,
                        provider_name,
                        provider_map[provider_name],
                    );
                });
            });
        }
    },

    populateExistingLinks: (container, provider_name, subs) => {
        container.querySelectorAll(".sso-provider-link-checkbox-wrapper").forEach((e) => e.remove());

        const checkboxes = subs.map((sub) => {
            const out = document.createElement("label");
            out.classList.add("sso-provider-link-checkbox-wrapper");
            out.classList.add("checkbox-wrapper");
            out.innerHTML = `
        <input
          is="emby-checkbox"
          class="sso-link-checkbox"
          data-sub="${sub}"
          data-provider="${provider_name}"
          type="checkbox"
        />
        <span class="checkbox-label">${sub}</span>
      `;
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

    view.querySelector("#enable-delete").addEventListener("change", (e) => {
        view.querySelector("#btn-delete-selected-links").disabled = !e.target.checked;
    });

    view.querySelector("#btn-delete-selected-links").addEventListener("click", (e) =>
        ssoConfigLinking.handleDeleteButtonPressed(e, view),
    );
}
