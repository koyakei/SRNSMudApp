window.selectionHelper = {
    _lastSelectedText: "",

    getSelectedText: function () {
        var selection = window.getSelection();
        var current = selection ? selection.toString() : "";
        if (current && current.trim().length > 0) {
            this._lastSelectedText = current;
            return current;
        }
        return this._lastSelectedText || "";
    },

    clearSelection: function () {
        this._lastSelectedText = "";
        var selection = window.getSelection();
        if (selection) {
            selection.removeAllRanges();
        }
    }
};

document.addEventListener("selectionchange", function () {
    var selection = window.getSelection();
    if (selection) {
        var text = selection.toString();
        if (text && text.trim().length > 0) {
            window.selectionHelper._lastSelectedText = text;
        }
    }
});

document.addEventListener("mousedown", function (e) {
    var target = e.target;
    var btn = target && target.closest ? target.closest("button[data-testid*='split-button'], button[title*='分割']") : null;
    if (btn) {
        // 分割ボタン押下時にブラウザのテキスト選択範囲が解除されるのを防止する
        e.preventDefault();
    }
});

