(() => {
    const frame = document.querySelector(".shell-frame");
    const mobile = matchMedia("(max-width: 767px)");
    const overlayViewport = matchMedia("(max-width: 1023px)");
    const themeButtons = document.querySelectorAll("[data-theme-toggle]");
    const settings = document.querySelector("#shell-settings");
    const headerToggle = frame?.querySelector(".shell-header [data-sidebar-toggle]");
    const sidebar = frame?.querySelector(".shell-sidebar");
    const nav = sidebar?.querySelector(".shell-navigation");
    const backdrop = frame?.querySelector(".shell-backdrop");
    const main = frame?.querySelector(".workspace-content");
    const header = frame?.querySelector(".shell-header");
    const submenus = [...(nav?.querySelectorAll(".shell-submenu") ?? [])];
    const accountMenus = [...document.querySelectorAll(".shell-account")];
    const command = document.querySelector("#shell-command");
    const query = command?.querySelector("[data-command-input]");
    const results = [...(command?.querySelectorAll("[data-command-results] li") ?? [])];
    let preferredExpanded = frame?.dataset.sidebarExpanded === "true";
    let overlayOpen = false;
    let flyout;
    let tooltip;
    let tooltipAnchor;

    function saveCookie(name, value) {
        const secure = location.protocol === "https:" ? "; Secure" : "";
        document.cookie = `${name}=${value}; Path=/; Max-Age=31536000; SameSite=Lax${secure}`;
    }

    function applyTheme(theme, persist = false) {
        frame.dataset.shellTheme = theme;
        if (persist) saveCookie("workspace_theme", theme);
        themeButtons.forEach(button => {
            button.setAttribute("aria-label", theme === "dark" ? "Switch to light mode" : "Switch to dark mode");
        });
        settings?.querySelectorAll('input[name="shell-theme"]').forEach(input => {
            input.checked = input.value === theme;
        });
    }

    function closeFlyout() {
        if (!flyout) return;
        flyout.removeAttribute("data-flyout");
        flyout.open = false;
        flyout = null;
    }

    function renderSidebar() {
        if (!frame || !sidebar) return;
        const expanded = overlayViewport.matches ? overlayOpen : preferredExpanded;
        const overlay = expanded && overlayViewport.matches;
        frame.dataset.sidebarExpanded = String(expanded);
        frame.toggleAttribute("data-overlay-open", overlay);
        backdrop.hidden = !overlay;
        sidebar.inert = mobile.matches && !overlayOpen;
        if (overlay) {
            sidebar.setAttribute("role", "dialog");
            sidebar.setAttribute("aria-modal", "true");
        } else {
            sidebar.removeAttribute("role");
            sidebar.removeAttribute("aria-modal");
        }
        header.inert = overlay;
        main.inert = overlay;
        frame.querySelectorAll("[data-sidebar-toggle]").forEach(button => button.setAttribute("aria-expanded", String(expanded)));
        settings?.querySelectorAll('input[name="shell-sidebar-style"]').forEach(input => {
            input.checked = input.value === String(preferredExpanded);
        });
        closeFlyout();
        hideTooltip();
        if (expanded) {
            const active = submenus.find(item => item.classList.contains("has-current"));
            if (active && !submenus.some(item => item.open)) active.open = true;
        } else {
            submenus.forEach(item => { item.open = false; });
        }
    }

    function toggleSidebar() {
        if (!frame) return;
        if (overlayViewport.matches) {
            overlayOpen = !overlayOpen;
        } else {
            preferredExpanded = !preferredExpanded;
            saveCookie("sidebar_state", preferredExpanded);
        }
        renderSidebar();
        if (!backdrop.hidden) sidebar.querySelector(".shell-collapse")?.focus();
        else if (!headerToggle.matches(":focus")) headerToggle.focus();
    }

    function closeOverlay() {
        if (!frame || backdrop.hidden) return;
        toggleSidebar();
    }

    function showFlyout(item) {
        if (frame.dataset.sidebarExpanded === "true" || flyout === item) return;
        closeFlyout();
        const row = item.querySelector("summary");
        item.style.setProperty("--flyout-top", `${Math.max(8, Math.min(row.getBoundingClientRect().top, innerHeight - 160))}px`);
        item.setAttribute("data-flyout", "");
        item.open = true;
        flyout = item;
    }

    function hideTooltip() {
        tooltip?.remove();
        tooltip = null;
        tooltipAnchor = null;
    }

    function showTooltip(anchor) {
        if (frame.dataset.sidebarExpanded === "true" || tooltipAnchor === anchor) return;
        hideTooltip();
        const bounds = anchor.getBoundingClientRect();
        tooltip = document.createElement("div");
        tooltip.className = "shell-tooltip";
        tooltip.setAttribute("aria-hidden", "true");
        tooltip.textContent = anchor.dataset.tip;
        tooltip.style.left = `${bounds.right + 11}px`;
        tooltip.style.top = `${anchor.closest(".shell-submenu") ? Math.max(4, bounds.top - 28) : bounds.top + 5}px`;
        frame.append(tooltip);
        tooltipAnchor = anchor;
    }

    function closeAccounts() {
        accountMenus.forEach(menu => { menu.open = false; });
    }

    function openDialog(dialog, trigger) {
        if (!dialog || dialog.open) return;
        closeAccounts();
        if (dialog === command) {
            query.value = "";
            filterResults();
        }
        dialog.showModal();
        if (dialog === command) query.focus();
        if (trigger) {
            dialog.addEventListener("close", () => {
                const account = trigger.closest(".shell-account");
                const returnTarget = account?.querySelector("summary") ?? trigger;
                if (returnTarget.closest("[inert]") && !backdrop.hidden) sidebar.querySelector(".shell-collapse")?.focus();
                else returnTarget.focus();
            }, { once: true });
        }
    }

    function filterResults() {
        const term = query.value.trim().toLocaleLowerCase();
        results.forEach(item => { item.hidden = !item.textContent.toLocaleLowerCase().includes(term); });
        command.querySelector("[data-command-empty]").hidden = results.some(item => !item.hidden);
    }

    function moveFocus(event, elements) {
        const index = elements.indexOf(event.target);
        const next = event.key === "ArrowDown" ? index + 1 : index < 0 ? elements.length - 1 : index - 1;
        elements[(next + elements.length) % elements.length]?.focus();
    }

    if (!frame) return;
    applyTheme(frame.dataset.shellTheme);
    themeButtons.forEach(button => button.addEventListener("click", () => applyTheme(frame.dataset.shellTheme === "dark" ? "light" : "dark", true)));
    renderSidebar();
    if (!backdrop.hidden) sidebar.querySelector(".shell-collapse")?.focus();
    mobile.addEventListener("change", renderSidebar);
    overlayViewport.addEventListener("change", () => { overlayOpen = false; renderSidebar(); });
    frame.querySelectorAll("[data-sidebar-toggle]").forEach(button => button.addEventListener("click", toggleSidebar));
    backdrop.addEventListener("click", closeOverlay);
    nav.addEventListener("scroll", () => { hideTooltip(); if (flyout && !flyout.contains(document.activeElement)) closeFlyout(); });
    nav.querySelectorAll("a[href]").forEach(link => link.addEventListener("click", closeOverlay));
    settings.addEventListener("change", event => {
        if (event.target.name === "shell-theme") applyTheme(event.target.value, true);
        if (event.target.name === "shell-sidebar-style") {
            preferredExpanded = event.target.value === "true";
            saveCookie("sidebar_state", preferredExpanded);
            if (overlayViewport.matches) overlayOpen = preferredExpanded;
            renderSidebar();
        }
    });

    submenus.forEach(item => {
        const summary = item.querySelector("summary");
        item.addEventListener("pointerenter", event => { if (event.pointerType === "mouse") showFlyout(item); });
        item.addEventListener("pointerleave", () => {
            if (flyout === item && !item.contains(document.activeElement)) closeFlyout();
        });
        item.addEventListener("focusout", event => {
            if (flyout === item && !item.contains(event.relatedTarget)) closeFlyout();
        });
        summary.addEventListener("click", event => {
            if (frame.dataset.sidebarExpanded === "true") return;
            event.preventDefault();
            showFlyout(item);
            item.querySelector(".shell-submenu-links a")?.focus();
        });
    });
    frame.querySelectorAll("[data-tip]").forEach(anchor => {
        anchor.addEventListener("pointerenter", event => { if (event.pointerType === "mouse") showTooltip(anchor); });
        anchor.addEventListener("pointerleave", hideTooltip);
        anchor.addEventListener("focus", () => showTooltip(anchor));
        anchor.addEventListener("blur", hideTooltip);
    });

    document.querySelectorAll("[data-open-dialog]").forEach(button => button.addEventListener("click", () => openDialog(document.getElementById(button.dataset.openDialog), button)));
    document.querySelectorAll(".shell-dialog").forEach(dialog => {
        dialog.querySelector("[data-close-dialog]").addEventListener("click", () => dialog.close());
        dialog.addEventListener("click", event => {
            if (event.target !== dialog) return;
            const bounds = dialog.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
        });
    });
    document.addEventListener("pointerdown", event => {
        accountMenus.filter(menu => !menu.contains(event.target)).forEach(menu => { menu.open = false; });
    });
    query.addEventListener("input", filterResults);
    command.addEventListener("keydown", event => {
        const links = results.filter(item => !item.hidden).map(item => item.querySelector("a"));
        if (["ArrowUp", "ArrowDown"].includes(event.key)) {
            event.preventDefault();
            moveFocus(event, links);
        }
        if (event.key === "Enter" && event.target === query) {
            event.preventDefault();
            links[0]?.click();
        }
    });
    nav.addEventListener("keydown", event => {
        const item = event.target.closest(".shell-submenu");
        const row = event.target.closest(".shell-nav-row");
        if (["ArrowUp", "ArrowDown"].includes(event.key)) {
            event.preventDefault();
            const selector = row || !item ? ".shell-nav-row" : ".shell-submenu-links a";
            moveFocus(event, [...(row || !item ? nav : item).querySelectorAll(selector)]);
        }
        if (item && event.key === "ArrowRight" && row) {
            event.preventDefault();
            if (frame.dataset.sidebarExpanded === "false") showFlyout(item);
            else item.open = true;
            item.querySelector(".shell-submenu-links a")?.focus();
        }
        if (item && event.key === "ArrowLeft" && item.open) {
            event.preventDefault();
            closeFlyout();
            item.open = false;
            item.querySelector("summary").focus();
        }
        if (event.key === " " && event.target.matches("a")) {
            event.preventDefault();
            event.target.click();
        }
    });
    document.addEventListener("keydown", event => {
        const modal = document.querySelector("dialog[open]");
        const shortcut = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey;
        if (shortcut && event.key.toLowerCase() === "j") {
            event.preventDefault();
            if (!modal) openDialog(command, document.activeElement);
        } else if (shortcut && event.key.toLowerCase() === "b" && !modal) {
            event.preventDefault();
            toggleSidebar();
        }
        if (modal) return;
        if (event.key === "Escape") {
            if (flyout) {
                const summary = flyout.querySelector("summary");
                closeFlyout();
                summary.focus();
            } else if (accountMenus.some(menu => menu.open)) {
                const open = accountMenus.find(menu => menu.open);
                closeAccounts();
                open.querySelector("summary").focus();
            } else closeOverlay();
        }
        if (event.key === "Tab" && !backdrop.hidden) {
            const focusable = [...sidebar.querySelectorAll("a[href], summary, button")].filter(element => element.getClientRects().length > 0 && getComputedStyle(element).visibility === "visible");
            const edge = event.shiftKey ? focusable.at(0) : focusable.at(-1);
            if (event.target === edge) {
                event.preventDefault();
                (event.shiftKey ? focusable.at(-1) : focusable.at(0))?.focus();
            }
        }
    });
})();
