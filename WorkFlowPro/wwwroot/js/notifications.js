/**
 * UC-11: SignalR realtime + toast 5s + badge + dropdown preview.
 */
(function () {
    const toastDelayMs = 5000;

    // Normalize redirectUrl for local dev:
    // If DB/old data stores absolute https://localhost/... while the app runs on http://localhost,
    // browser will throw ERR_SSL_PROTOCOL_ERROR. Convert it to relative path.
    function normalizeRedirectUrl(href) {
        if (!href) return href;
        try {
            const u = new URL(href, window.location.origin);
            if (u.hostname === "localhost" || u.hostname === "127.0.0.1") {
                return `${u.pathname}${u.search}${u.hash}`;
            }
        } catch (_) {
            // ignore
        }
        return href;
    }

    function escapeHtml(s) {
        if (s === null || s === undefined) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function showToast(payload) {
        const container = document.getElementById("wfNotificationToastContainer");
        if (!container || typeof bootstrap === "undefined" || !bootstrap.Toast) return;

        const el = document.createElement("div");
        el.className = "toast align-items-center text-bg-primary border-0";
        el.setAttribute("role", "alert");
        el.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">${escapeHtml(payload.message)}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>`;
        container.appendChild(el);
        const toast = new bootstrap.Toast(el, { delay: toastDelayMs });
        toast.show();
        el.addEventListener("hidden.bs.toast", () => el.remove());

        if (payload.redirectUrl) {
            const redirect = normalizeRedirectUrl(payload.redirectUrl);
            el.style.cursor = "pointer";
            el.addEventListener("click", function (e) {
                if (e.target.closest("button")) return;
                window.location.href = redirect;
            });
        }
    }

    async function fetchUnreadCount() {
        try {
            const res = await fetch("/api/notifications/unread-count", { credentials: "include" });
            if (!res.ok) return;
            const n = await res.json();
            const badge = document.getElementById("wfNotifBadge");
            if (!badge) return;
            if (n > 0) {
                badge.textContent = n > 99 ? "99+" : String(n);
                badge.style.display = "";
            } else {
                badge.style.display = "none";
            }
        } catch (e) {
            console.warn(e);
        }
    }

    async function loadDropdownPreview() {
        const body = document.getElementById("wfNotifDropdownBody");
        if (!body) return;
        body.innerHTML = '<span class="text-muted"><span class="spinner-border spinner-border-sm" role="status"></span> Đang tải…</span>';
        try {
            const res = await fetch("/api/notifications?take=20", { credentials: "include" });
            if (!res.ok) {
                body.innerHTML = '<span class="text-danger">Không tải được danh sách.</span>';
                return;
            }
            const items = await res.json();
            if (!items.length) {
                body.innerHTML = '<span class="text-muted">Không có thông báo.</span>';
                return;
            }
            body.innerHTML = items
                .map(function (m) {
                    const unread = m.isRead === false ? '<span class="badge bg-danger me-1">Mới</span>' : "";
                    const dt = new Date(m.createdAtUtc).toLocaleString();
                    const href = normalizeRedirectUrl(m.redirectUrl) || "#";
                    return `<div class="mb-2 pb-2 border-bottom">
                        <a href="${escapeHtml(href)}" class="text-decoration-none text-dark wf-notif-link" data-id="${m.id}">
                            ${unread}<span class="small text-muted">${escapeHtml(dt)}</span><br/>
                            <span class="small">${escapeHtml(m.message)}</span>
                        </a>
                    </div>`;
                })
                .join("");

            body.querySelectorAll(".wf-notif-link").forEach(function (a) {
                a.addEventListener("click", async function (e) {
                    const id = a.getAttribute("data-id");
                    if (!id) return;
                    try {
                        await fetch("/api/notifications/" + id + "/read", { method: "POST", credentials: "include" });
                        await fetchUnreadCount();
                    } catch (_) { /* ignore */ }
                });
            });
        } catch (e) {
            console.warn(e);
            body.innerHTML = '<span class="text-danger">Lỗi mạng.</span>';
        }
    }

    async function markAllRead() {
        try {
            await fetch("/api/notifications/read-all", { method: "POST", credentials: "include" });
            await fetchUnreadCount();
            await loadDropdownPreview();
        } catch (e) {
            console.warn(e);
        }
    }

    async function startSignalR() {
        if (typeof signalR === "undefined") return;

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/notification")
            .withAutomaticReconnect()
            .build();

        connection.on("notification", function (payload) {
            showToast(payload);
            fetchUnreadCount();
        });

        try {
            await connection.start();
        } catch (e) {
            console.warn("NotificationHub:", e);
        }
    }

    function wireUi() {
        const bell = document.getElementById("wfNotificationBellWrapper");
        if (!bell) return;

        bell.addEventListener("show.bs.dropdown", function () {
            loadDropdownPreview();
        });

        const btnAll = document.getElementById("wfNotifMarkAllRead");
        if (btnAll) btnAll.addEventListener("click", function (e) {
            e.preventDefault();
            e.stopPropagation();
            markAllRead();
        });

        fetchUnreadCount();
        startSignalR();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", wireUi);
    } else {
        wireUi();
    }
})();
