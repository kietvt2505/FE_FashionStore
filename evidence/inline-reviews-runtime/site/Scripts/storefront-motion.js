(function () {
    "use strict";
    var home = document.querySelector(".yody-home-refresh");
    if (!home || !window.matchMedia || !Element.prototype.animate) return;

    var preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    var active = [];
    var observer;

    // Animate visible elements without hiding content in CSS: the page also works without JS.
    function enter(element, delay, distance) {
        if (preference.matches) return;
        var animation = element.animate([
            { opacity: .25, transform: "translateY(" + distance + "px)" },
            { opacity: 1, transform: "translateY(0)" }
        ], { duration: 600, delay: delay, easing: "cubic-bezier(.2,.65,.3,1)", fill: "backwards" });
        active.push(animation);
        animation.onfinish = function () { active = active.filter(function (item) { return item !== animation; }); };
    }

    if (!preference.matches) {
        var intro = home.querySelectorAll(".hp-hero-copy > .hp-kicker,.hp-hero-copy > h1,.hp-hero-copy > p:not(.hp-kicker),.hp-hero-actions");
        Array.prototype.forEach.call(intro, function (element, index) { enter(element, index * 85, 16); });

        if ("IntersectionObserver" in window) {
            observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (!entry.isIntersecting) return;
                    observer.unobserve(entry.target);
                    // Do not replay sections above the viewport when restoring browser scroll position.
                    if (entry.boundingClientRect.bottom > 0) enter(entry.target, 0, 18);
                });
            }, { threshold: .08 });
            Array.prototype.forEach.call(home.querySelectorAll(".hp-section-heading,.hp-product-rail > .product-card,.hp-collection,.hp-sale-grid > .product-card,.hp-denim,.hp-quote,.hp-inspiration-card"), function (element) {
                observer.observe(element);
            });
        }
    }

    function stopMotion() {
        if (observer) observer.disconnect();
        active.forEach(function (animation) { animation.cancel(); });
        active = [];
    }
    function onPreferenceChange() { if (preference.matches) stopMotion(); }
    if (preference.addEventListener) preference.addEventListener("change", onPreferenceChange);
    else if (preference.addListener) preference.addListener(onPreferenceChange);
    window.addEventListener("beforeprint", stopMotion);
}());
