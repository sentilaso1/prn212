/**
 * UC-14 → UC-04: Khi MemberProfile (Level/SubRole) đổi, làm mới bảng gợi ý phân công không mất nội dung form.
 */
(function () {
    const root = document.getElementById("wfCreateTaskRoot");
    if (!root) return;

    const wsRaw = root.getAttribute("data-workspace-id");
    if (!wsRaw) return;

    function escapeHtml(s) {
        if (s === null || s === undefined) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function formatLevel(level) {
        if (typeof level === "string") return level;
        const m = { 1: "Junior", 2: "Mid", 3: "Senior" };
        return m[level] !== undefined ? m[level] : String(level);
    }

    function formatPct(x) {
        const n = Number(x);
        if (Number.isNaN(n)) return "0%";
        return Math.round(n * 100) + "%";
    }

    function formatScore(x) {
        const n = Number(x);
        if (Number.isNaN(n)) return "0.0";
        return n.toFixed(1);
    }

    async function refreshSuggestions() {
        const sel = document.querySelector('select[name="Input.Priority"]');
        const priority = sel ? sel.value : "2";
        const container = document.getElementById("wfCreateTaskSuggestions");
        if (!container) return;

        try {
            const res = await fetch(
                "/api/tasks/suggested-assignees?priority=" + encodeURIComponent(priority),
                { credentials: "include" }
            );
            if (!res.ok) return;
            const data = await res.json();
            if (!Array.isArray(data) || data.length === 0) {
                container.innerHTML =
                    '<div class="alert alert-info">Chưa có member đủ điều kiện để gợi ý phân công.</div>';
                return;
            }

            let rows = "";
            for (let i = 0; i < data.length; i++) {
                const s = data[i];
                const uid = escapeHtml(s.userId);
                const name = escapeHtml(s.displayName);
                rows +=
                    "<tr>" +
                    '<td style="width:90px;"><div class="form-check">' +
                    '<input class="form-check-input" type="radio" name="Input.AssigneeUserId" value="' +
                    uid +
                    '"/>' +
                    "</div></td>" +
                    "<td>" +
                    name +
                    "</td>" +
                    "<td>" +
                    escapeHtml(formatLevel(s.level)) +
                    "</td>" +
                    "<td>" +
                    escapeHtml(formatPct(s.completionRate)) +
                    "</td>" +
                    "<td>" +
                    escapeHtml(formatScore(s.avgScore)) +
                    "</td>" +
                    "<td>" +
                    escapeHtml(String(s.currentWorkload)) +
                    "</td>" +
                    "</tr>";
            }

            container.innerHTML =
                '<div class="mb-3">' +
                '<div class="form-check">' +
                '<input class="form-check-input" type="radio" name="Input.AssigneeUserId" value="" checked />' +
                '<label class="form-check-label">Không gán ai (task ở trạng thái Unassigned)</label>' +
                "</div></div>" +
                '<table class="table table-sm table-striped">' +
                "<thead><tr><th>Select</th><th>Name</th><th>Level</th><th>Completion Rate</th><th>AvgScore</th><th>CurrentWorkload</th></tr></thead>" +
                '<tbody id="wfSuggestedAssigneesBody">' +
                rows +
                "</tbody></table>" +
                '<div class="form-text">Gợi ý dựa trên `MemberProfile` của member (CurrentWorkload thấp, CompletionRate/AvgScore cao, Level phù hợp).</div>' +
                '<div class="alert alert-success py-2 mt-2 small mb-0">Đã làm mới gợi ý sau khi cập nhật profile member.</div>';
        } catch (e) {
            console.warn("refreshSuggestions failed", e);
        }
    }

    if (typeof signalR === "undefined") {
        console.warn("SignalR not loaded");
        return;
    }

    const conn = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/task")
        .withAutomaticReconnect()
        .build();

    conn.on("memberProfileUpdated", function () {
        refreshSuggestions();
    });

    conn.start()
        .then(function () {
            return conn.invoke("JoinWorkspace", wsRaw);
        })
        .catch(function (e) {
            console.warn("TaskHub join failed", e);
        });
})();
