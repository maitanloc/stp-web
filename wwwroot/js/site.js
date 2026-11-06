(function () {
    "use strict";

    function norm(text) {
        return (text || "")
            .toString()
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .replace(/đ/g, "d")
            .replace(/Đ/g, "D")
            .toLowerCase()
            .trim();
    }

    function showMode(mode) {
        ["all", "range", "custom"].forEach(function (item) {
            var panel = document.getElementById("panel-" + item);
            if (panel) {
                panel.classList.toggle("hidden", item !== mode);
            }

            var card = document.getElementById("card-mode-" + item);
            if (!card) {
                return;
            }

            if (item === mode) {
                card.classList.add("bg-blue-50/60", "border-brand-600", "border-2");
                card.classList.remove("bg-white", "border-slate-300", "border");
            } else {
                card.classList.remove("bg-blue-50/60", "border-brand-600", "border-2");
                card.classList.add("bg-white", "border-slate-300", "border");
            }
        });
    }

    window.stpShowMode = showMode;

    window.stpModeChanged = function (mode) {
        showMode(mode);
        var form = document.getElementById("filterForm");
        if (form) {
            form.submit();
        }
    };

    window.stpChangeBook = function () {
        var all = document.querySelector('input[name="Mode"][value="all"]');
        if (all) {
            all.checked = true;
        }
        var form = document.getElementById("filterForm");
        if (form) {
            form.submit();
        }
    };

    window.stpApplyFilter = function () {
        var form = document.getElementById("filterForm");
        if (form) {
            form.submit();
        }
    };

    window.stpSetPreset = function (type) {
        var meta = document.getElementById("bookMeta");
        if (!meta) {
            return;
        }

        var min = parseInt(meta.dataset.min || "0", 10);
        var max = parseInt(meta.dataset.max || "0", 10);
        var fromInput = document.getElementById("input-from");
        var toInput = document.getElementById("input-to");
        var allRadio = document.querySelector('input[name="Mode"][value="all"]');
        var rangeRadio = document.querySelector('input[name="Mode"][value="range"]');

        if (type === "all") {
            if (allRadio) {
                allRadio.checked = true;
            }
        } else if (type === "first50") {
            if (rangeRadio) {
                rangeRadio.checked = true;
            }
            if (fromInput) {
                fromInput.value = min;
            }
            if (toInput) {
                toInput.value = Math.min(max, min + 49);
            }
        } else if (type === "latest") {
            if (rangeRadio) {
                rangeRadio.checked = true;
            }
            if (fromInput) {
                fromInput.value = Math.max(min, max - 99);
            }
            if (toInput) {
                toInput.value = max;
            }
        }

        var form = document.getElementById("filterForm");
        if (form) {
            form.submit();
        }
    };

    function updateVisibleCount() {
        var table = document.getElementById("previewTable");
        if (!table) {
            return;
        }

        var rows = Array.prototype.slice.call(table.querySelectorAll("tbody tr[data-search]"));
        var visible = rows.filter(function (row) {
            return !row.classList.contains("hidden");
        }).length;

        var badge = document.getElementById("table-count-badge");
        if (badge) {
            badge.textContent = "Hiển thị " + visible + " / " + rows.length + " hồ sơ";
        }

        var empty = document.getElementById("table-empty");
        if (empty) {
            empty.classList.toggle("hidden", visible !== 0);
        }
    }

    window.stpFilterTable = function () {
        var input = document.getElementById("table-search");
        if (!input) {
            return;
        }

        var query = norm(input.value);
        var table = document.getElementById("previewTable");
        if (!table) {
            return;
        }

        table.querySelectorAll("tbody tr[data-search]").forEach(function (row) {
            var haystack = norm(row.dataset.search || row.textContent);
            row.classList.toggle("hidden", query.length > 0 && haystack.indexOf(query) === -1);
        });

        updateVisibleCount();
    };

    window.stpFileChosen = function (input) {
        var label = document.getElementById("file-name");
        if (label) {
            label.textContent = input && input.files && input.files.length > 0
                ? input.files[0].name
                : "Chưa chọn file";
        }
    };

    window.stpImportSubmitting = function (form) {
        var button = document.getElementById("btn-import");
        if (button) {
            button.disabled = true;
            button.innerHTML = '<i class="fa-solid fa-spinner fa-spin mr-2"></i>Đang đọc và import...';
        }
        return true;
    };

    window.stpConfirmClear = function () {
        return window.confirm("Xóa toàn bộ dữ liệu đã import? Bạn sẽ cần import lại file Excel.");
    };

    document.addEventListener("DOMContentLoaded", function () {
        var form = document.getElementById("filterForm");
        if (form) {
            var checked = form.querySelector('input[name="Mode"]:checked');
            showMode(checked ? checked.value : "all");
        }

        var search = document.getElementById("table-search");
        if (search) {
            search.addEventListener("input", window.stpFilterTable);
            window.stpFilterTable();
        }
    });
})();
