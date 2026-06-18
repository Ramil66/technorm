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
    }
};
