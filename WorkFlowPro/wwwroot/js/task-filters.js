/**
 * UC-16: Filter & sort tasks — Kanban + Task List (AJAX, Session lưu phía server).
 */
(function () {
    const ALL_STATUSES = ["Unassigned", "Pending", "ToDo", "InProgress", "Review", "Done", "Cancelled"];
    const ALL_PRI = ["Low", "Medium", "High", "Critical"];

    function escapeHtml(s) {
        if (s === null || s === undefined) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    /** SignalR / JSON .NET có thể trả PascalCase — chuẩn hóa trước khi render. */
    function normalizeTaskCardShape(c) {
        if (!c) return null;
        return {
            taskId: c.taskId ?? c.TaskId,
            title: c.title ?? c.Title ?? "",
            projectId: c.projectId ?? c.ProjectId,
            priority: c.priority ?? c.Priority ?? "",
            dueDateUtc: c.dueDateUtc ?? c.DueDateUtc,
            status: c.status ?? c.Status ?? "",
            assigneeUserId: c.assigneeUserId ?? c.AssigneeUserId ?? "",
            assigneeDisplayName: c.assigneeDisplayName ?? c.AssigneeDisplayName ?? "",
            assigneeAvatarUrl: c.assigneeAvatarUrl ?? c.AssigneeAvatarUrl
        };
    }

    function wfTaskCardHtml(card, myUserId, isPm) {
        if (!card) return "";
        card = normalizeTaskCardShape(card);
        if (!card) return "";
        const st = (card.status || "").toString();
        const draggableStatuses = ["ToDo", "InProgress", "Review", "Done"];
        const canDrag =
            draggableStatuses.includes(st) && (isPm || (card.assigneeUserId && card.assigneeUserId === myUserId));
        const due = card.dueDateUtc ? new Date(card.dueDateUtc) : null;
        const now = new Date();
        const isDone = st === "Done";
        const isOverdue = !!(due && !isDone && due.getTime() < now.getTime());

        const priority = (card.priority || "").toLowerCase();
        let badgeClass = "bg-secondary";
        if (priority.includes("low")) badgeClass = "bg-success";
        else if (priority.includes("medium")) badgeClass = "bg-primary";
        else if (priority.includes("high")) badgeClass = "bg-warning text-dark";
        else if (priority.includes("critical")) badgeClass = "bg-danger";

        const avatar = card.assigneeAvatarUrl
            ? `<img src="${escapeHtml(card.assigneeAvatarUrl)}" class="wf-avatar" alt="avatar" />`
            : `<div class="wf-avatar wf-avatar--text">${escapeHtml((card.assigneeDisplayName || "?").substring(0, 1).toUpperCase())}</div>`;

        const dueStr = due ? due.toISOString().slice(0, 10) : "-";

        return `
            <div class="task-card card wf-task-card mb-2 ${isOverdue ? "border-danger" : ""}"
                 data-task-id="${card.taskId}"
                 data-title="${escapeHtml(card.title)}"
                 data-status="${escapeHtml(st)}"
                 data-can-drag="${canDrag ? "1" : "0"}"
                 draggable="${canDrag ? "true" : "false"}">
                <div class="card-body p-2">
                    <div class="d-flex justify-content-between align-items-start gap-2">
                        <a href="/Tasks/Details/${card.taskId}" class="text-decoration-none">
                            <div class="fw-semibold" style="max-width: 220px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">
                                ${escapeHtml(card.title)}
                            </div>
                        </a>
                        <span class="badge ${badgeClass}">${escapeHtml(card.priority)}</span>
                    </div>
                    <div class="mt-1 small text-muted">
                        Due: <span class="${isOverdue ? "text-danger fw-semibold" : ""}">${escapeHtml(dueStr)}</span>
                    </div>
                    <div class="mt-2 d-flex align-items-center gap-2">
                        ${avatar}
                        <div class="small">${escapeHtml(card.assigneeDisplayName || "-")}</div>
                    </div>
                </div>
            </div>`;
    }

    function collectCriteria(root) {
        const st = [...root.querySelectorAll(".wf-filter-status-cb:checked")].map((x) => x.value);
        const statuses = st.length === 0 ? [] : st.length === ALL_STATUSES.length ? null : st;

        const pr = [...root.querySelectorAll(".wf-filter-pri-cb:checked")].map((x) => x.value);
        const priorities = pr.length === 0 ? [] : pr.length === ALL_PRI.length ? null : pr;

        const assigneeSel = root.querySelector(".wf-filter-assignees");
        const assigneeUserIds =
            assigneeSel && assigneeSel.selectedOptions.length > 0
                ? [...assigneeSel.selectedOptions].map((o) => o.value)
                : null;

        const due = root.querySelector(".wf-filter-due")?.value ?? "None";
        const sort = root.querySelector(".wf-filter-sort")?.value ?? "DueDateAsc";
        const rawSearch = root.querySelector(".wf-filter-search")?.value?.trim();
        const searchTitle = rawSearch ? rawSearch : null;

        return {
            assigneeUserIds: assigneeUserIds,
            statuses: statuses,
            priorities: priorities,
            dueDateBucket: due,
            sort: sort,
            searchTitle: searchTitle
        };
    }

    function applyCriteriaToForm(root, c) {
        if (!c) return;
        const assigneeSel = root.querySelector(".wf-filter-assignees");
        if (assigneeSel) {
            const set = new Set(c.assigneeUserIds || []);
            [...assigneeSel.options].forEach((o) => {
                o.selected = set.has(o.value);
            });
        }

        const stSet = c.statuses === null ? null : new Set(c.statuses || []);
        root.querySelectorAll(".wf-filter-status-cb").forEach((cb) => {
            if (c.statuses === null || c.statuses === undefined) cb.checked = true;
            else cb.checked = stSet.has(cb.value);
        });

        const prSet = c.priorities === null ? null : new Set(c.priorities || []);
        root.querySelectorAll(".wf-filter-pri-cb").forEach((cb) => {
            if (c.priorities === null || c.priorities === undefined) cb.checked = true;
            else cb.checked = prSet.has(cb.value);
        });

        const due = root.querySelector(".wf-filter-due");
        if (due) due.value = c.dueDateBucket || "None";

        const sort = root.querySelector(".wf-filter-sort");
        if (sort) sort.value = c.sort || "DueDateAsc";

        const search = root.querySelector(".wf-filter-search");
        if (search) search.value = c.searchTitle || "";
    }

    function updateKanbanColumns(payload, myUserId, isPm) {
        const map = [
            ["unassigned", "wf-stack-unassigned"],
            ["pending", "wf-stack-pending"],
            ["toDo", "wf-drop-todo"],
            ["inProgress", "wf-drop-inprogress"],
            ["review", "wf-drop-review"],
            ["done", "wf-drop-done"]
        ];

        const counts = [
            ["wf-count-unassigned", payload.unassigned],
            ["wf-count-pending", payload.pending],
            ["wf-count-todo", payload.toDo],
            ["wf-count-inprogress", payload.inProgress],
            ["wf-count-review", payload.review],
            ["wf-count-done", payload.done]
        ];

        counts.forEach(([id, arr]) => {
            const el = document.getElementById(id);
            if (el && arr) el.textContent = arr.length;
        });

        map.forEach(([key, elId]) => {
            const host = document.getElementById(elId);
            if (!host || !payload[key]) return;
            host.innerHTML = (payload[key] || []).map((c) => wfTaskCardHtml(c, myUserId, isPm)).join("");
        });
    }

    function updateTaskListTable(tasks, myUserId, isPm) {
        const body = document.getElementById("wfTaskListBody");
        if (!body) return;
        body.innerHTML = (tasks || [])
            .map((t) => {
                const due = t.dueDateUtc ? new Date(t.dueDateUtc).toISOString().slice(0, 10) : "-";
                return `<tr>
                    <td><a href="/Tasks/Details/${t.taskId}">${escapeHtml(t.title)}</a></td>
                    <td><span class="badge bg-secondary">${escapeHtml(t.status)}</span></td>
                    <td>${escapeHtml(t.priority)}</td>
                    <td>${escapeHtml(due)}</td>
                    <td>${escapeHtml(t.assigneeDisplayName || "-")}</td>
                </tr>`;
            })
            .join("");
    }

    async function postJson(url, body) {
        const res = await fetch(url, {
            method: "POST",
            credentials: "include",
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const txt = await res.text();
            throw new Error(txt || res.statusText);
        }
        return res.json();
    }

    function debounce(fn, ms) {
        let t;
        return function () {
            clearTimeout(t);
            const args = arguments;
            t = setTimeout(() => fn.apply(null, args), ms);
        };
    }

    function bindFilterCard(root, opts) {
        const projectId = root.getAttribute("data-project-id");
        const view = root.getAttribute("data-view") || "kanban";
        if (!projectId) return;

        const run = debounce(async () => {
            const criteria = collectCriteria(root);
            try {
                if (view === "kanban") {
                    const data = await postJson(`/api/projects/${projectId}/tasks/filter-kanban`, criteria);
                    updateKanbanColumns(data, opts.myUserId, opts.isPm);
                    if (typeof opts.onKanbanUpdated === "function") opts.onKanbanUpdated();
                } else {
                    const data = await postJson(`/api/projects/${projectId}/tasks/filter-list`, criteria);
                    updateTaskListTable(data.tasks, opts.myUserId, opts.isPm);
                }
            } catch (e) {
                console.error(e);
                alert("Không thể áp dụng bộ lọc. " + (e.message || ""));
            }
        }, 280);

        root.querySelectorAll(
            ".wf-filter-assignees, .wf-filter-due, .wf-filter-sort, .wf-filter-search"
        ).forEach((el) => el.addEventListener("change", run));
        root.querySelectorAll(".wf-filter-search").forEach((el) => el.addEventListener("input", run));
        root.querySelectorAll(".wf-filter-status-cb, .wf-filter-pri-cb").forEach((el) =>
            el.addEventListener("change", run)
        );

        const clearBtn = root.querySelector(".wf-filter-clear");
        if (clearBtn) {
            clearBtn.addEventListener("click", async () => {
                try {
                    const res = await fetch(`/api/projects/${projectId}/tasks/filter-reset`, {
                        method: "POST",
                        credentials: "include"
                    });
                    if (!res.ok) throw new Error(await res.text());
                    const def = await res.json();
                    applyCriteriaToForm(root, def);
                    if (view === "kanban") {
                        const data = await postJson(`/api/projects/${projectId}/tasks/filter-kanban`, def);
                        updateKanbanColumns(data, opts.myUserId, opts.isPm);
                        if (typeof opts.onKanbanUpdated === "function") opts.onKanbanUpdated();
                    } else {
                        const data = await postJson(`/api/projects/${projectId}/tasks/filter-list`, def);
                        updateTaskListTable(data.tasks, opts.myUserId, opts.isPm);
                    }
                } catch (e) {
                    console.error(e);
                    alert("Không thể xóa bộ lọc.");
                }
            });
        }
    }

    /** Gọi lại API list theo form lọc hiện tại — dùng khi SignalR báo task đổi (trang /tasks/list). */
    async function refreshTaskListForProject(root, opts) {
        const projectId = root.getAttribute("data-project-id");
        const view = root.getAttribute("data-view") || "list";
        if (!projectId || view !== "list") return;
        try {
            const criteria = collectCriteria(root);
            const data = await postJson(`/api/projects/${projectId}/tasks/filter-list`, criteria);
            updateTaskListTable(data.tasks, opts.myUserId, opts.isPm);
        } catch (e) {
            console.warn("refreshTaskListForProject", e);
        }
    }

    /** Đồng bộ lại cả board từ server theo bộ lọc (fallback sau kéo Kanban / taskMoved). */
    async function refreshKanbanForProject(root, opts) {
        const projectId = root.getAttribute("data-project-id");
        const view = root.getAttribute("data-view") || "kanban";
        if (!projectId || view !== "kanban") return;
        try {
            const criteria = collectCriteria(root);
            const data = await postJson(`/api/projects/${projectId}/tasks/filter-kanban`, criteria);
            updateKanbanColumns(data, opts.myUserId, opts.isPm);
            if (typeof opts.onKanbanUpdated === "function") opts.onKanbanUpdated();
        } catch (e) {
            console.warn("refreshKanbanForProject", e);
        }
    }

    window.wfTaskFilters = {
        collectCriteria,
        applyCriteriaToForm,
        updateKanbanColumns,
        wfTaskCardHtml,
        normalizeTaskCardShape,
        refreshTaskListForProject,
        refreshKanbanForProject,
        bindFilterCard
    };
})();
