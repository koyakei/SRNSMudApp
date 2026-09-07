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

        const createAddChildButton = (nodeId) => {
            const $btn = $('<span class="add-child-btn mud-icon-root mud-svg-icon mud-primary-text" style="cursor:pointer; margin-left:8px; vertical-align:middle; width: 1.25em; height: 1.25em; display:inline-block;" title="子タグを追加">' +
                '<svg focusable="false" viewBox="0 0 24 24" aria-hidden="true"><path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"></path></svg>' +
                '</span>');
            $btn.on('click', function (e) {
                e.stopPropagation();
                // noinspection JSUnresolvedReference
                window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('AddChildTagByNodeId', nodeId);
            });
            return $btn;
        };

        const createCancelButton = (requestId) => {
            const $btn = $('<span class="cancel-move-btn mud-icon-root mud-svg-icon mud-error-text" style="cursor:pointer; margin-left:8px; vertical-align:middle; width: 1.25em; height: 1.25em; display:inline-block;" title="移動リクエストをキャンセル">' +
                '<svg focusable="false" viewBox="0 0 24 24" aria-hidden="true"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"></path></svg>' +
                '</span>');
            $btn.on('click', function (e) {
                e.stopPropagation();
                // noinspection JSUnresolvedReference
                window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('CancelMoveRequest', requestId);
            });
            return $btn;
        };

        $tree.tree({
            data,
            autoOpen: true,
            dragAndDrop: isLoggedIn,
            onCanMove(node) {
                return isLoggedIn && !node.isPendingMove;
            },
            onCanMoveTo(moved_node, target_node, position) {
                return !target_node.isPendingMove;
            },
            /** @public */
            // noinspection JSUnusedGlobalSymbols
            onCreateLi(node, $li) {
                if (node.isPendingMove) {
                    $li.addClass('pending-move-node');
                    const $title = $li.find('.jqtree-title');
                    $title.addClass('pending-move-title');
                    $title.after('<span class="pending-move-badge mud-chip mud-chip-filled mud-chip-size-small mud-chip-color-warning" style="margin-left: 6px; font-size: 0.72rem; padding: 1px 6px; height: 18px; border-radius: 9px; vertical-align: middle; display: inline-flex; align-items: center;">移動申請中</span>');
                    if (isLoggedIn && node.canCancel) {
                        $title.parent().append(createCancelButton(node.requestId));
                    }
                    return;
                }

                if (isLoggedIn) {
                    const $title = $li.find('.jqtree-title');
                    $title.before('<input type="checkbox" class="tag-checkbox" data-id="' + node.id + '" style="margin-right: 8px; cursor: pointer;" />');
                    $title.after(createAddChildButton(node.id));
                }
            }
        });

        if (selectedNodeId) {
            const node = $tree.tree('getNodeById', selectedNodeId);
            if (node) {
                $tree.tree('selectNode', node);
                const $nodeLi = $(node.element);
                if ($nodeLi.length) {
                    $nodeLi[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            }
        }

        $tree.on('tree.select', function (event) {
            if (event.node && event.node.isPendingMove) {
                return;
            }
            // Notify C# of the selection change to update the URL query.
            // Child buttons are rendered per node in onCreateLi so they remain visible across all tags.
            const selectedNodeId = event.node ? event.node.id : null;
            // noinspection JSUnresolvedReference
            window.jqTreeInterop.dotNetHelpers[elementId].invokeMethodAsync('OnNodeSelected', selectedNodeId);
        });

        $tree.on('tree.click', function (event) {
            if (event.node && event.node.isPendingMove) {
                event.preventDefault();
                return;
            }

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

            if (moved_node.isPendingMove || target_node.isPendingMove) {
                return;
            }

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
