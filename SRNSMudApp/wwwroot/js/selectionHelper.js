window.selectionHelper = {
    getSelectedText: function () {
        var selection = window.getSelection();
        if (!selection) return "";
        return selection.toString();
    }
};
