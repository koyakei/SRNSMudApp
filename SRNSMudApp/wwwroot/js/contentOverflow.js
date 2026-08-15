/* noinspection JSUnusedGlobalSymbols */
// ItemList コンポーネント用: コンテンツの視覚的オーバーフロー検出ヘルパー
window.contentOverflowHelper = {
    _dotNetRefs: [],
    _resizeHandler: null,
    _observer: null,
    _scrollHandler: null,
    _scrollTimeout: null,
    _isProgrammaticScroll: false,

    /**
     * 初期化: resize リスナーを登録する。
     */
    init(dotNetRef) {
        if (!this._dotNetRefs.includes(dotNetRef)) {
            this._dotNetRefs.push(dotNetRef);
        }
        if (!this._resizeHandler) {
            this._resizeHandler = this._onResize.bind(this);
            window.addEventListener('resize', this._resizeHandler);
        }
    },

    /**
     * スクロールオブザーバーの初期化
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    initScrollObserver() {
        if (this._observer) return;

        const options = {
            root: null,
            rootMargin: "-40% 0px -40% 0px", // 画面の中央20%
            threshold: 0
        };

        this._observer = new IntersectionObserver(entries => {
            if (this._isProgrammaticScroll) return;

            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    this._dotNetRefs.forEach(ref => ref.invokeMethodAsync('OnElementFocusedByScroll', entry.target.id).catch(() => {
                    }));
                }
            });
        }, options);

        // スクロール端検出: ページの最上部・最下部に到達したとき、
        // IntersectionObserver の中央ゾーンに入らない端の要素にフォーカスするフォールバック
        this._scrollHandler = this._onScroll.bind(this);
        // MudBlazor のように特定のコンテナがスクロールする場合も検知できるよう capture: true にする
        window.addEventListener('scroll', this._scrollHandler, true);
    },

    /**
     * スクロール端検出ハンドラ (debounce 150ms)
     */
    _onScroll(e) {
        clearTimeout(this._scrollTimeout);
        this._scrollTimeout = setTimeout(() => {
            if (this._isProgrammaticScroll || this._dotNetRefs.length === 0) return;

            let scrollContainer = e.target;
            if (scrollContainer === document || scrollContainer === window) {
                scrollContainer = document.documentElement;
            }

            // 要素がスクロール可能なコンテナでない場合はスキップ
            if (scrollContainer.scrollHeight <= scrollContainer.clientHeight) {
                return;
            }

            const scrollTop = scrollContainer.scrollTop;
            const scrollHeight = scrollContainer.scrollHeight;
            const clientHeight = scrollContainer.clientHeight;
            // Safariなどのバウンススクロールや、少数のピクセル誤差を吸収するために余裕を持たせる
            const threshold = 5;

            const targets = document.querySelectorAll('.scroll-observe-target');
            if (targets.length === 0) return;

            if (Math.ceil(scrollTop + clientHeight) >= scrollHeight - threshold) {
                // 最下部到達 → 最後の要素にフォーカス
                const lastEl = targets[targets.length - 1];
                if (lastEl && lastEl.id) {
                    this._dotNetRefs.forEach(ref => ref.invokeMethodAsync('OnElementFocusedByScroll', lastEl.id).catch(() => {
                    }));
                }
            } else if (scrollTop <= threshold) {
                // 最上部到達 → 最初の要素にフォーカス
                const firstEl = targets[0];
                if (firstEl && firstEl.id) {
                    this._dotNetRefs.forEach(ref => ref.invokeMethodAsync('OnElementFocusedByScroll', firstEl.id).catch(() => {
                    }));
                }
            }
        }, 150);
    },

    /**
     * 指定された要素をオブザーバーの監視対象に追加
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    observeElements(selector) {
        if (!this._observer) return;
        const elements = document.querySelectorAll(selector);
        elements.forEach(el => this._observer.observe(el));
    },

    /**
     * 指定された要素IDリストについて、line-clamp で溢れているかチェックする。
     * 溢れている（scrollHeight > clientHeight）要素の ID を返す。
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    checkOverflow(ids) {
        const result = [];
        for (let i = 0; i < ids.length; i++) {
            const el = document.getElementById('item-content-' + ids[i]);
            if (el && el.scrollHeight > el.clientHeight + 1) {
                result.push(ids[i]);
            }
        }
        return result;
    },

    /**
     * resize イベントハンドラ (debounce 200ms)
     */
    _timeout: null,
    _onResize() {
        clearTimeout(this._timeout);
        this._timeout = setTimeout(function () {
            this._dotNetRefs.forEach(ref => ref.invokeMethodAsync('OnWindowResized').catch(() => {
            }));
        }.bind(this), 200);
    },

    /**
     * 指定された要素（ポップオーバー内の要素など）が表示されるようにスクロールする
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    scrollToElement(selector) {
        this._isProgrammaticScroll = true;
        setTimeout(function () {
            const el = document.querySelector(selector);
            if (el) {
                el.scrollIntoView({behavior: 'auto', block: 'center'});
            }
            setTimeout(function () {
                window.contentOverflowHelper._isProgrammaticScroll = false;
            }, 300);
        }, 50); // DOMにポップオーバーが描画されるのを少し待つ
    },

    /**
     * リスナー解除
     */
    dispose() {
        if (this._resizeHandler) {
            window.removeEventListener('resize', this._resizeHandler);
            this._resizeHandler = null;
        }
        if (this._scrollHandler) {
            window.removeEventListener('scroll', this._scrollHandler, true);
            this._scrollHandler = null;
        }
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
        this._dotNetRefs = [];
    },

    removeDotNetRef(dotNetRef) {
        this._dotNetRefs = this._dotNetRefs.filter(ref => ref !== dotNetRef);
    }
};
