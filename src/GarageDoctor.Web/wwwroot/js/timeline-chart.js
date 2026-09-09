(function () {
    "use strict";

    function tokens() {
        var styles = window.getComputedStyle(document.documentElement);

        function read(name, fallback) {
            var value = styles.getPropertyValue(name);
            return value && value.trim() ? value.trim() : fallback;
        }

        return {
            line: read("--bar", "#5e716b"),
            ink: read("--ink", "#171614"),
            faint: read("--ink-3", "#7d776f"),
            rule: read("--rule", "#e2dfd8"),
            sans: read("--font-sans", "system-ui, sans-serif"),
            mono: read("--font-mono", "ui-monospace, monospace")
        };
    }

    function payloadOf(canvas) {
        var raw = canvas.getAttribute("data-timeline");

        if (!raw) {
            return null;
        }

        try {
            var parsed = JSON.parse(raw);
            return parsed && Array.isArray(parsed.years) && parsed.years.length > 0 ? parsed : null;
        } catch (error) {
            return null;
        }
    }

    function draw(canvas, palette) {
        var payload = payloadOf(canvas);

        if (!payload) {
            return false;
        }

        new window.Chart(canvas, {
            type: "line",
            data: {
                labels: payload.years,
                datasets: [{
                    data: payload.counts,
                    borderColor: palette.line,
                    backgroundColor: palette.line,
                    borderWidth: 1.5,
                    pointRadius: 2,
                    pointHoverRadius: 4,
                    tension: 0,
                    fill: false
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: palette.ink,
                        cornerRadius: 0,
                        displayColors: false,
                        padding: 8,
                        titleFont: { family: palette.mono },
                        bodyFont: { family: palette.mono },
                        callbacks: {
                            label: function (item) {
                                return item.formattedValue + " reports";
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { color: palette.rule },
                        ticks: {
                            color: palette.faint,
                            maxRotation: 0,
                            autoSkipPadding: 14,
                            font: { family: palette.mono, size: 11 }
                        }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: palette.rule, drawTicks: false },
                        border: { display: false },
                        ticks: {
                            color: palette.faint,
                            precision: 0,
                            padding: 8,
                            font: { family: palette.mono, size: 11 }
                        }
                    }
                }
            }
        });

        return true;
    }

    function boot() {
        if (typeof window.Chart === "undefined") {
            return;
        }

        var palette = tokens();
        window.Chart.defaults.font.family = palette.sans;
        window.Chart.defaults.color = palette.faint;

        var frames = document.querySelectorAll("[data-timeline-frame]");

        for (var index = 0; index < frames.length; index++) {
            var frame = frames[index];
            var canvas = frame.querySelector("[data-timeline]");

            if (!canvas) {
                continue;
            }

            frame.hidden = false;

            if (!draw(canvas, palette)) {
                frame.hidden = true;
            }
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }
})();
