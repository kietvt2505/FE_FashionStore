(function () {
    "use strict";
    var root = document.querySelector(".yody-home-refresh");
    if (!root) return;
    var rail = root.querySelector("[data-home-rail]");
    var controls = root.querySelector("[data-home-controls]");
    if (!rail || !controls) return;
    var previous = controls.querySelector("[data-home-prev]");
    var next = controls.querySelector("[data-home-next]");
    var reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    function update() {
        var overflow = rail.scrollWidth - rail.clientWidth;
        controls.hidden = overflow < 2;
        previous.disabled = rail.scrollLeft <= 1;
        next.disabled = rail.scrollLeft >= overflow - 2;
    }
    function move(direction) {
        var card = rail.querySelector(".product-card");
        if (!card) return;
        var gap = parseFloat(window.getComputedStyle(rail).columnGap) || 0;
        var step = card.getBoundingClientRect().width + gap;
        var visible = Math.max(1, Math.floor((rail.clientWidth + gap + 1) / step));
        rail.scrollBy({ left: direction * visible * step, behavior: reducedMotion.matches ? "auto" : "smooth" });
    }
    previous.addEventListener("click", function () { move(-1); });
    next.addEventListener("click", function () { move(1); });
    rail.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    window.addEventListener("load", update);
    update();
}());
