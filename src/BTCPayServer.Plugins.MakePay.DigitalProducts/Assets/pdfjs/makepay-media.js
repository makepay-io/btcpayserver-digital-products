(() => {
    "use strict";

    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    const setReaderStatus = (reader, message, isError = false) => {
        const status = reader.querySelector("[data-pdf-status]");
        if (!status) return;
        const statusText = status.querySelector("[data-pdf-status-text]");
        if (statusText) statusText.textContent = message;
        else status.textContent = message;
        status.classList.toggle("is-error", isError);
    };

    const initPdfReader = async reader => {
        if (reader.dataset.initialized === "true" || reader.dataset.loading === "true") return;
        reader.dataset.loading = "true";
        setReaderStatus(reader, "Opening preview…");

        try {
            const pdfModule = await import(reader.dataset.library);
            pdfModule.GlobalWorkerOptions.workerSrc = reader.dataset.worker;
            const task = pdfModule.getDocument({
                url: reader.dataset.source,
                withCredentials: true,
                disableAutoFetch: false,
                disableStream: false
            });
            const pdf = await task.promise;
            const canvas = reader.querySelector("canvas");
            const context = canvas.getContext("2d", { alpha: false });
            const pageInput = reader.querySelector("[data-pdf-page]");
            const total = reader.querySelector("[data-pdf-total]");
            const previous = reader.querySelector("[data-pdf-previous]");
            const next = reader.querySelector("[data-pdf-next]");
            const zoomOut = reader.querySelector("[data-pdf-zoom-out]");
            const zoomIn = reader.querySelector("[data-pdf-zoom-in]");
            const zoomValue = reader.querySelector("[data-pdf-zoom-value]");
            let pageNumber = 1;
            let zoom = 1;
            let renderTask;

            pageInput.max = String(pdf.numPages);
            total.textContent = String(pdf.numPages);

            const render = async requestedPage => {
                pageNumber = Math.min(pdf.numPages, Math.max(1, requestedPage));
                previous.disabled = pageNumber <= 1;
                next.disabled = pageNumber >= pdf.numPages;
                pageInput.value = String(pageNumber);
                zoomValue.textContent = `${Math.round(zoom * 100)}%`;
                setReaderStatus(reader, `Rendering page ${pageNumber} of ${pdf.numPages}…`);
                if (renderTask) {
                    try { renderTask.cancel(); } catch (_) { }
                }
                const page = await pdf.getPage(pageNumber);
                const baseViewport = page.getViewport({ scale: 1 });
                const stage = reader.querySelector("[data-pdf-stage]");
                const availableWidth = Math.max(280, stage.clientWidth - 40);
                const fitScale = Math.min(2, availableWidth / baseViewport.width);
                const viewport = page.getViewport({ scale: fitScale * zoom });
                const ratio = Math.min(2, window.devicePixelRatio || 1);
                canvas.width = Math.floor(viewport.width * ratio);
                canvas.height = Math.floor(viewport.height * ratio);
                canvas.style.width = `${Math.floor(viewport.width)}px`;
                canvas.style.height = `${Math.floor(viewport.height)}px`;
                const transform = ratio === 1 ? null : [ratio, 0, 0, ratio, 0, 0];
                renderTask = page.render({ canvasContext: context, transform, viewport });
                try {
                    await renderTask.promise;
                    setReaderStatus(reader, `Page ${pageNumber} of ${pdf.numPages}`);
                } catch (error) {
                    if (error?.name !== "RenderingCancelledException") throw error;
                }
            };

            previous.addEventListener("click", () => render(pageNumber - 1));
            next.addEventListener("click", () => render(pageNumber + 1));
            pageInput.addEventListener("change", () => render(Number(pageInput.value) || 1));
            zoomOut.addEventListener("click", () => { zoom = Math.max(.65, zoom - .15); render(pageNumber); });
            zoomIn.addEventListener("click", () => { zoom = Math.min(2.2, zoom + .15); render(pageNumber); });
            reader.querySelector("[data-pdf-fullscreen]")?.addEventListener("click", async () => {
                if (!document.fullscreenElement) await reader.requestFullscreen?.();
                else await document.exitFullscreen?.();
            });
            document.addEventListener("fullscreenchange", () => {
                if (reader.dataset.initialized === "true" && (document.fullscreenElement === reader || !document.fullscreenElement)) render(pageNumber);
            });
            reader.addEventListener("keydown", event => {
                if (event.target.matches("input, button, a")) return;
                if (event.key === "ArrowLeft") { event.preventDefault(); render(pageNumber - 1); }
                if (event.key === "ArrowRight") { event.preventDefault(); render(pageNumber + 1); }
            });
            reader.dataset.initialized = "true";
            await render(1);
        } catch (error) {
            console.error("MakePay PDF preview failed", error);
            setReaderStatus(reader, "The preview could not be opened. Use the direct preview link instead.", true);
            reader.querySelector("[data-pdf-fallback]")?.removeAttribute("hidden");
        } finally {
            delete reader.dataset.loading;
        }
    };

    const schedulePdfReader = reader => {
        const disclosure = reader.closest("details");
        if (disclosure && !disclosure.open) {
            disclosure.addEventListener("toggle", () => {
                if (disclosure.open) initPdfReader(reader);
            });
            return;
        }
        initPdfReader(reader);
    };

    document.querySelectorAll("[data-pdf-reader]").forEach(schedulePdfReader);

    document.querySelectorAll("[data-photo-gallery]").forEach(gallery => {
        const image = gallery.querySelector("[data-photo-main]");
        const caption = gallery.querySelector("[data-photo-caption]");
        const thumbs = [...gallery.querySelectorAll("[data-photo-thumb]")];
        if (!image || thumbs.length === 0) return;
        const activate = (button, focus = false) => {
            thumbs.forEach(item => item.setAttribute("aria-selected", item === button ? "true" : "false"));
            image.src = button.dataset.source;
            image.alt = button.dataset.alt || "Product preview";
            if (caption) caption.textContent = button.dataset.label || image.alt;
            if (!prefersReducedMotion) image.animate([{ opacity: .45 }, { opacity: 1 }], { duration: 180, easing: "ease-out" });
            if (focus) button.focus();
        };
        thumbs.forEach((button, index) => {
            button.addEventListener("click", () => activate(button));
            button.addEventListener("keydown", event => {
                if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
                event.preventDefault();
                const offset = event.key === "ArrowRight" ? 1 : -1;
                activate(thumbs[(index + offset + thumbs.length) % thumbs.length], true);
            });
        });
    });
})();
