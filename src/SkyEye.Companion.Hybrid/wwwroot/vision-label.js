(() => {
    const bindings = new WeakMap();

    window.skyEyeVisionLabel = {
        attach(surface, dotNet) {
            if (!surface || bindings.has(surface)) return;
            let start = null;
            const preview = document.createElement("div");
            preview.className = "vision-label-drag";

            const point = event => {
                const rect = surface.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) return null;
                return {
                    x: Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)),
                    y: Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height))
                };
            };
            const paint = end => {
                if (!start) return;
                const left = Math.min(start.x, end.x), top = Math.min(start.y, end.y);
                preview.style.left = `${left * 100}%`;
                preview.style.top = `${top * 100}%`;
                preview.style.width = `${Math.abs(end.x - start.x) * 100}%`;
                preview.style.height = `${Math.abs(end.y - start.y) * 100}%`;
                if (!preview.isConnected) surface.appendChild(preview);
            };
            const onDown = event => {
                if (event.button !== undefined && event.button !== 0) return;
                const at = point(event);
                if (!at) return;
                event.preventDefault();
                start = at;
                surface.setPointerCapture?.(event.pointerId);
                paint(at);
            };
            const onMove = event => {
                if (!start) return;
                event.preventDefault();
                const at = point(event);
                if (at) paint(at);
            };
            const onUp = event => {
                if (!start) return;
                event.preventDefault();
                const first = start;
                const last = point(event);
                start = null;
                preview.remove();
                if (last && Math.abs(last.x - first.x) >= 0.002 && Math.abs(last.y - first.y) >= 0.002)
                    dotNet.invokeMethodAsync("OnBoxDrawn", first.x, first.y, last.x, last.y);
            };
            const onCancel = () => { start = null; preview.remove(); };
            surface.addEventListener("pointerdown", onDown, { passive: false });
            surface.addEventListener("pointermove", onMove, { passive: false });
            surface.addEventListener("pointerup", onUp, { passive: false });
            surface.addEventListener("pointercancel", onCancel);
            bindings.set(surface, () => {
                surface.removeEventListener("pointerdown", onDown);
                surface.removeEventListener("pointermove", onMove);
                surface.removeEventListener("pointerup", onUp);
                surface.removeEventListener("pointercancel", onCancel);
                preview.remove();
            });
        },
        detach(surface) {
            const dispose = surface && bindings.get(surface);
            if (dispose) dispose();
            if (surface) bindings.delete(surface);
        }
    };
})();
