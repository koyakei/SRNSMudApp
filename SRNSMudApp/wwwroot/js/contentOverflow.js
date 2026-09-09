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
     * DOMへの描画遅延やBlazorのレンダリングによるレイアウトシフトに対応するためリトライと位置追従を行う
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    scrollToElement(selector) {
        this._isProgrammaticScroll = true;
        let attempts = 0;
        const maxAttempts = 30; // 50ms * 30 = 最大1.5秒待機

        const tryScroll = () => {
            const el = document.querySelector(selector);
            if (el) {
                // 要素を表示領域の中央にスクロール
                el.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });

                // 遅延レンダリングや画像読み込みによるレイアウトシフト（位置ズレ）を補正するため
                // 150ms後、400ms後、800ms後に再チェックして微調整する
                const checkAndAdjust = () => {
                    const rect = el.getBoundingClientRect();
                    const windowHeight = window.innerHeight || document.documentElement.clientHeight;
                    // 上端が画面外（ヘッダー下含む）または下端が画面外の場合は再調整
                    if (rect.top < 64 || rect.bottom > windowHeight) {
                        el.scrollIntoView({ behavior: 'auto', block: 'center', inline: 'nearest' });
                    }
                };

                setTimeout(checkAndAdjust, 150);
                setTimeout(checkAndAdjust, 400);
                setTimeout(() => {
                    checkAndAdjust();
                    window.contentOverflowHelper._isProgrammaticScroll = false;
                }, 800);
            } else if (attempts < maxAttempts) {
                attempts++;
                setTimeout(tryScroll, 50);
            } else {
                window.contentOverflowHelper._isProgrammaticScroll = false;
            }
        };

        setTimeout(tryScroll, 50);
    },

    /**
     * URL のクエリパラメータを Blazor のルーティングをトリガーせずにブラウザ履歴上書きで更新する (debounce 100ms)
     * @public
     */
    // noinspection JSUnusedGlobalSymbols
    _urlUpdateTimeout: null,
    updateUrl(url) {
        clearTimeout(this._urlUpdateTimeout);
        this._urlUpdateTimeout = setTimeout(() => {
            if (window.history && window.history.replaceState) {
                window.history.replaceState(null, '', url);
            }
        }, 100);
    },

    /**
     * リスナー解除
     */
    dispose() {
        clearTimeout(this._urlUpdateTimeout);
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
