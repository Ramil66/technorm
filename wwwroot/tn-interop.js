window.tnInterop = {

    initBarChart: function (canvasId, labels, data) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const existing = Chart.getChart(canvas);
        if (existing) existing.destroy();
        new Chart(canvas, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: 'События',
                    data: data,
                    backgroundColor: 'rgba(37,99,235,0.75)',
                    hoverBackgroundColor: 'rgba(37,99,235,0.95)',
                    borderRadius: 5,
                    borderSkipped: false,
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: { stepSize: 1, font: { size: 12 } },
                        grid: { color: 'rgba(0,0,0,0.05)' }
                    },
                    x: {
                        ticks: { font: { size: 12 } },
                        grid: { display: false }
                    }
                }
            }
        });
    },

    confirmDelete: function (message) {
        return window.confirm(message || 'Удалить запись?');
    },

    scrollToTop: function () {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    },

    downloadJson: function (filename, content) {
        const blob = new Blob([content], { type: 'application/json;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    setupInfiniteScroll: function (dotnetRef, sentinelId) {
        if (window._tnInfiniteObserver) {
            window._tnInfiniteObserver.disconnect();
        }
        const sentinel = document.getElementById(sentinelId);
        if (!sentinel) return;
        window._tnInfiniteObserver = new IntersectionObserver(function (entries) {
            if (entries[0].isIntersecting) {
                dotnetRef.invokeMethodAsync('OnScrolledToBottom');
            }
        }, { rootMargin: '400px' });
        window._tnInfiniteObserver.observe(sentinel);
    },

    disposeInfiniteScroll: function () {
        if (window._tnInfiniteObserver) {
            window._tnInfiniteObserver.disconnect();
            window._tnInfiniteObserver = null;
        }
    },

    showNavProgress: function () {
        const bar = document.getElementById('tn-nav-progress');
        if (!bar) return;
        clearTimeout(window._tnNavTimer);
        bar.style.transition = 'none';
        bar.style.width = '0';
        bar.style.opacity = '1';
        requestAnimationFrame(function () {
            bar.style.transition = 'width 0.4s ease';
            bar.style.width = '50%';
            window._tnNavTimer = setTimeout(function () {
                if (bar.style.opacity === '1') {
                    bar.style.transition = 'width 0.8s ease';
                    bar.style.width = '82%';
                }
            }, 450);
        });
    },

    hideNavProgress: function () {
        const bar = document.getElementById('tn-nav-progress');
        if (!bar || bar.style.opacity !== '1') return;
        clearTimeout(window._tnNavTimer);
        bar.style.transition = 'width 0.12s ease';
        bar.style.width = '100%';
        setTimeout(function () {
            bar.style.transition = 'opacity 0.22s ease';
            bar.style.opacity = '0';
            setTimeout(function () { bar.style.width = '0'; }, 230);
        }, 120);
    },

    // Живая перестановка шагов маршрута мышью: пока идёт drag, перетаскиваемая
    // карточка физически переносится в DOM на место вставки (как в sortable-
    // библиотеках), а соседние карточки плавно "раздвигаются" через FLIP.
    // Всё происходит на клиенте без обращений к серверу — это и чинит DnD
    // (Blazor Server не успевает патчить DOM во время самого перетаскивания)
    // и даёт нужный визуальный эффект.
    routeDrag: {
        draggedEl: null,
        containerId: null,
        lastRef: undefined,

        start: function (el, containerId) {
            this.draggedEl = el;
            this.containerId = containerId;
            this.lastRef = el.nextElementSibling;
            el.classList.add('dragging');
        },

        over: function (containerId, clientX) {
            if (!this.draggedEl || containerId !== this.containerId) return;
            const container = document.getElementById(containerId);
            if (!container) return;

            const cards = Array.from(container.querySelectorAll('.mk-step-card')).filter(c => c !== this.draggedEl);
            let refNode = null;
            for (const card of cards) {
                const rect = card.getBoundingClientRect();
                if (clientX < rect.left + rect.width / 2) { refNode = card; break; }
            }
            if (refNode === this.lastRef) return;
            this.lastRef = refNode;

            const before = {};
            container.querySelectorAll('[data-step-id]').forEach(function (c) {
                before[c.dataset.stepId] = c.getBoundingClientRect();
            });

            if (refNode) container.insertBefore(this.draggedEl, refNode);
            else container.appendChild(this.draggedEl);

            const draggedEl = this.draggedEl;
            container.querySelectorAll('[data-step-id]').forEach(function (c) {
                if (c === draggedEl) return; // сама карточка просто "прыгает" за курсором, FLIP ей не нужен
                const from = before[c.dataset.stepId];
                if (!from) return;
                const to = c.getBoundingClientRect();
                const dx = from.left - to.left;
                const dy = from.top - to.top;
                if (Math.abs(dx) < 1 && Math.abs(dy) < 1) return;

                c.style.transition = 'none';
                c.style.transform = 'translate(' + dx + 'px, ' + dy + 'px)';
                c.getBoundingClientRect(); // форсируем reflow перед сменой transition
                requestAnimationFrame(function () {
                    c.style.transition = 'transform .22s cubic-bezier(.22,.8,.24,1)';
                    c.style.transform = '';
                    c.addEventListener('transitionend', function () {
                        c.style.transition = '';
                    }, { once: true });
                });
            });
        },

        end: function () {
            if (this.draggedEl) this.draggedEl.classList.remove('dragging');
            this.draggedEl = null;
            this.containerId = null;
            this.lastRef = undefined;
        },

        getOrder: function (containerId) {
            const container = document.getElementById(containerId);
            if (!container) return [];
            return Array.from(container.querySelectorAll('[data-step-id]')).map(function (el) {
                return parseInt(el.dataset.stepId, 10);
            });
        }
    },

    // Лёгкая всплывающая подсказка для SVG-графиков (без Blazor round-trip):
    // текст задаётся сразу при рендере SVG на сервере, здесь только позиционируем.
    showChartTooltip: function (evt, tooltipId, text) {
        const tip = document.getElementById(tooltipId);
        if (!tip) return;
        tip.textContent = text;
        tip.style.display = 'block';
        window.tnInterop.moveChartTooltip(evt, tooltipId);
    },

    moveChartTooltip: function (evt, tooltipId) {
        const tip = document.getElementById(tooltipId);
        const wrap = tip && tip.parentElement;
        if (!tip || !wrap) return;
        const rect = wrap.getBoundingClientRect();
        tip.style.left = (evt.clientX - rect.left + 12) + 'px';
        tip.style.top = (evt.clientY - rect.top - 10) + 'px';
    },

    hideChartTooltip: function (tooltipId) {
        const tip = document.getElementById(tooltipId);
        if (tip) tip.style.display = 'none';
    }
};

(function () {
    // Мгновенный отклик: показываем прогресс-бар при клике на внутренние ссылки
    document.addEventListener('click', function (e) {
        const a = e.target.closest('a[href]');
        if (!a || a.target === '_blank' || a.hasAttribute('download')) return;
        const href = a.getAttribute('href') || '';
        if (!href || href.startsWith('http') || href.startsWith('//') || href.startsWith('#') || href.startsWith('mailto:')) return;
        if (href === '/logout' || href.startsWith('/account/')) return;
        window.tnInterop.showNavProgress();
    }, true);

    // Скрываем когда Blazor меняет URL (навигация завершилась)
    const _origPush = history.pushState.bind(history);
    history.pushState = function (state, title, url) {
        _origPush(state, title, url);
        setTimeout(function () { window.tnInterop.hideNavProgress(); }, 60);
    };
})();
