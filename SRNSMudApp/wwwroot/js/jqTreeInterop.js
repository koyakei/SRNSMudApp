/* noinspection JSUnusedGlobalSymbols, JSUnresolvedReference */
window.jqTreeInterop = {
    dotNetHelpers: {},
    init(elementId, data, dotNetHelper, isLoggedIn, selectedNodeId) {
        window.jqTreeInterop.dotNetHelpers[elementId] = dotNetHelper;
        if (typeof data === 'string') {
            data = JSON.parse(data);
        }
        const $tree = $('#' + elementId);

        let lastChecked = null;
        $tree.off('click', '.tag-checkbox');
        $tree.on('click', '.tag-checkbox', function (e) {
            const $chkboxes = $tree.find('.tag-checkbox');
            if (!lastChecked) {
                lastChecked = this;
                return;
            }

            if (e.shiftKey) {
                const start = $chkboxes.index(this);
                const end = $chkboxes.index(lastChecked);
                const checkedStatus = this.checked;

                $chkboxes.slice(Math.min(start, end), Math.max(start, end) + 1).prop('checked', checkedStatus);
            }

            lastChecked = this;
        });

        $tree.tree({
            data,
            autoOpen: true,
            dragAndDrop: isLoggedIn,
            /** @public */
            // noinspection JSUnusedGlobalSymbols
            onCreateLi(node, $li) {
                // jqtree-titleの前にチェックボックスを挿入
                if (isLoggedIn) {
                    $li.find('.jqtree-title').before('<input type="checkbox" class="tag-checkbox" data-id="' + node.id + '" style="margin-right: 8px; cursor: pointer;" />');
                }
            }
        });

        if (selectedNodeId) {
            const node = $tree.tree('getNodeById', selectedNodeId);
            if (node) {
                $tree.tree('selectNode', node);
                const $nodeLi = $(node.element);
                if ($nodeLi.length) {
                    $nodeLi[0].scrollIntoView({behavior: 'smooth', block: 'center'});
                }
            }
        }

        $tree.on('tree.select', function (event) {
            $tree.find('.add-child-btn').remove();
            if (event.node && isLoggedIn) {
                const nodeId = event.node.id;
                const $title = $(event.node.element).find('.jqtree-title');
                const btnHtml = '<span class="add-child-btn mud-icon-root mud-svg-icon mud-primary-text" style="cursor:pointer; margin-left:8px; vertical-align:middle; width: 1.25em; height: 1.25em; display:inline-block;" title="子タグを追加">' +
                    '<svg focusable="false" viewBox="0 0 24 24" aria-hidden="true"><path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"></path></svg>' +
                    '</span>';
                const $btn = $(btnHtml);
                $btn.on('click', function (e) {
                    e.stopPropagation();
                    // noinspection JSUnresolvedReference
                    window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('AddChildTagByNodeId', nodeId);
                });
                $title.after($btn);
            }

            // Notify C# of the selection change to update the URL query
            const selectedNodeId = event.node ? event.node.id : null;
            // noinspection JSUnresolvedReference
            window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('OnNodeSelected', selectedNodeId);
        });

        $tree.on('tree.click', function (event) {
            // noinspection JSUnresolvedReference
            if (event.node && event.click_event) {
                // noinspection JSUnresolvedReference
                const $target = $(event.click_event.target);
                if ($target.hasClass('jqtree-title') || $target.closest('.jqtree-title').length > 0) {
                    // Prevent jqTree from selecting the node, which would fire tree.select 
                    // and overwrite the NavigationManager action.
                    event.preventDefault();

                    const nodeId = event.node.id;
                    // noinspection JSUnresolvedReference
                    window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('NavigateToTagDetail', nodeId);
                }
            }
        });

        $tree.on('tree.move', function (event) {
            // Cancel the default move immediately so Blazor takes over full state management
            event.preventDefault();

            const moved_node = event.move_info.moved_node;
            const target_node = event.move_info.target_node;
            const position = event.move_info.position;

            // Notify Blazor
            dotNetHelper.invokeMethodAsync('OnTreeMove', moved_node.id, target_node.id, position);
        });
    },
    /** @public */
    // noinspection JSUnusedGlobalSymbols
    loadData(elementId, data) {
        if (typeof data === 'string') {
            data = JSON.parse(data);
        }
        const $tree = $('#' + elementId);
        if ($tree.length) {
            $tree.tree('loadData', data);
            $tree.find('.add-child-btn').remove();
        }
    },
    destroy(elementId) {
        const $tree = $('#' + elementId);
        if ($tree.length) {
            $tree.tree('destroy');
        }
    },
    /** @public */
    // noinspection JSUnusedGlobalSymbols
    getSelectedIds(elementId) {
        const ids = [];
        $('#' + elementId).find('.tag-checkbox:checked').each(function () {
            const id = $(this).data('id');
            if (id) {
                ids.push(parseInt(id));
            }
        });
        return ids;
    }
};
