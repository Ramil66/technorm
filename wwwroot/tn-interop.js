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
