namespace MdViewer
{
    /// <summary>The script that runs inside the preview document. It powers the
    /// image viewer (click a picture to open it, zoom, pan, step through the
    /// document) and it keeps a small bridge element in sync so the native code
    /// can ask which image the pointer is over.
    ///
    /// The preview is drawn by the IE11 engine that ships with Windows 10, so
    /// the script stays inside ES5: no arrow functions, no let or const.</summary>
    internal static class PreviewScript
    {
        public const string Code = @"
(function () {
    if (window.md) { return; }

    var md = window.md = {};
    md.list = [];
    md.index = -1;
    md.zoom = 1;
    md.panX = 0;
    md.panY = 0;
    md.naturalW = 0;
    md.naturalH = 0;
    md.naturalSrc = '';
    md.visible = false;
    md.dragging = false;

    function el(id) { return document.getElementById(id); }

    function setBridge(src, alt, open) {
        var box = el('md-state');
        if (box) { box.innerText = (src || '') + '\u0001' + (alt || '') + '\u0001' + (open ? 'open' : ''); }
    }

    md.read = function () {
        var box = el('md-state');
        var raw = box ? (box.innerText || '') : '';
        var parts = raw.split('\u0001');
        return {
            src: parts[0] || '',
            alt: parts.length > 1 ? parts[1] : '',
            open: parts.length > 2 && parts[2] === 'open'
        };
    };

    /* The native side cannot read a value back out of the engine, so results
       travel through a hidden element instead. */
    window.mdEval = function (code) {
        var box = el('md-eval');
        if (!box) { return ''; }
        try {
            box.innerText = 'OK:' + String(eval(code));
        }
        catch (e) {
            box.innerText = 'ERR:' + (e && e.message ? e.message : 'error');
        }
    };

    window.mdSetStyle = function (id, css) {
        var old = el(id);
        if (old && old.parentNode) { old.parentNode.removeChild(old); }
        var style = document.createElement('style');
        style.setAttribute('id', id);
        style.appendChild(document.createTextNode(css));
        var head = document.getElementsByTagName('head')[0];
        if (!head) { head = document.body; }
        head.appendChild(style);
    };

    function markBroken(img) {
        if (!img) { return; }
        if ((' ' + img.className + ' ').indexOf(' md-broken ') < 0) {
            img.className = img.className ? img.className + ' md-broken' : 'md-broken';
        }
        if (!img.alt) { img.alt = 'image not found'; }
    }

    md.scan = function () {
        var host = el('out');
        md.list = [];
        if (!host) { return; }
        var found = host.getElementsByTagName('img');
        for (var i = 0; i < found.length; i++) {
            var img = found[i];
            md.list.push(img);
            img.onerror = function () { markBroken(this); };
            if (img.complete && img.width === 0) { markBroken(img); }
        }
    };

    function naturalSize(img, done) {
        if (md.naturalSrc === img.src && md.naturalW) { done(md.naturalW, md.naturalH); return; }
        var probe = new Image();
        probe.onload = function () {
            md.naturalSrc = img.src;
            md.naturalW = probe.width;
            md.naturalH = probe.height;
            done(md.naturalW, md.naturalH);
        };
        probe.onerror = function () { done(0, 0); };
        probe.src = img.src;
    }

    md.render = function () {
        var img = md.list[md.index];
        if (!img) { return; }

        var view = el('md-lb-img');
        var frame = el('md-lb-frame');
        var caption = el('md-lb-caption');
        var counter = el('md-lb-count');
        var zoomLabel = el('md-lb-zoom');
        if (!view || !frame) { return; }

        view.src = img.src;
        view.alt = img.alt || '';

        naturalSize(img, function (w, h) {
            if (!w || !h) { return; }
            var room = frame.clientWidth - 48;
            var tall = frame.clientHeight - 48;
            if (room < 40) { room = 40; }
            if (tall < 40) { tall = 40; }
            var fit = Math.min(1, Math.min(room / w, tall / h));
            if (fit <= 0) { fit = 1; }
            var width = Math.round(w * fit * md.zoom);
            var height = Math.round(h * fit * md.zoom);
            view.style.width = width + 'px';
            view.style.height = height + 'px';
            view.style.marginLeft = Math.round(-width / 2 + md.panX) + 'px';
            view.style.marginTop = Math.round(-height / 2 + md.panY) + 'px';
        });

        if (caption) {
            var text = img.alt || '';
            if (img.title) { text = text ? text + '  \u00b7  ' + img.title : img.title; }
            caption.innerText = text;
        }
        if (counter) { counter.innerText = (md.index + 1) + ' / ' + md.list.length; }
        if (zoomLabel) { zoomLabel.innerText = Math.round(md.zoom * 100) + '%'; }

        setBridge(img.src, img.alt, true);
    };

    md.open = function (index) {
        md.scan();
        if (index < 0 || index >= md.list.length) { return false; }
        md.index = index;
        md.zoom = 1;
        md.panX = 0;
        md.panY = 0;
        var box = el('md-lightbox');
        if (!box) { return false; }
        box.style.display = 'block';
        md.visible = true;
        md.render();
        return true;
    };

    md.close = function () {
        var box = el('md-lightbox');
        if (box) { box.style.display = 'none'; }
        md.visible = false;
        md.index = -1;
        var current = md.read();
        setBridge(current.src, current.alt, false);
    };

    md.openBySrc = function (src) {
        if (!src) { return false; }
        md.scan();
        for (var i = 0; i < md.list.length; i++) {
            if (md.list[i].src === src) { return md.open(i); }
        }
        return false;
    };

    md.step = function (delta) {
        if (md.index < 0 || md.list.length === 0) { return; }
        var next = (md.index + delta + md.list.length) % md.list.length;
        md.index = next;
        md.zoom = 1;
        md.panX = 0;
        md.panY = 0;
        md.render();
    };

    md.setZoom = function (value) {
        if (value < 0.2) { value = 0.2; }
        if (value > 8) { value = 8; }
        md.zoom = value;
        md.render();
    };

    md.zoomBy = function (factor) { md.setZoom(md.zoom * factor); };

    md.reset = function () {
        md.close();
        md.scan();
        setBridge('', '', false);
    };

    function findImage(target) {
        while (target && target.id !== 'out') {
            if (target.tagName && target.tagName.toLowerCase() === 'img') { return target; }
            target = target.parentNode;
        }
        return null;
    }

    function wire() {
        var host = el('out');
        if (host) {
            host.onclick = function (e) {
                e = e || window.event;
                var target = e.target || e.srcElement;
                var img = findImage(target);
                if (!img) { return true; }
                var node = img;
                while (node && node !== host) {
                    if (node.tagName && node.tagName.toLowerCase() === 'a') { return true; }
                    node = node.parentNode;
                }
                md.scan();
                for (var i = 0; i < md.list.length; i++) {
                    if (md.list[i] === img) { md.open(i); break; }
                }
                return false;
            };
        }

        function onContext(e) {
        e = e || window.event;
        var target = e.target || e.srcElement;
        var img = findImage(target);
        if (img) {
            setBridge(img.src, img.alt, md.visible);
        }
    }

    /* The native side owns the context menu, so this listener only records the
       picture under the pointer and never cancels the event. */
    if (document.attachEvent) { document.attachEvent('oncontextmenu', onContext); }
    else if (document.addEventListener) { document.addEventListener('contextmenu', onContext, false); }

        document.onkeydown = function (e) {
            if (!md.visible) { return true; }
            e = e || window.event;
            var code = e.keyCode;
            if (code === 27) { md.close(); return false; }
            if (code === 37) { md.step(-1); return false; }
            if (code === 39) { md.step(1); return false; }
            if (code === 43 || code === 61) { md.zoomBy(1.25); return false; }
            if (code === 45) { md.zoomBy(0.8); return false; }
            if (code === 48) { md.setZoom(1); md.panX = 0; md.panY = 0; md.render(); return false; }
            return true;
        };

        var box = el('md-lightbox');
        if (box) {
            box.onclick = function (e) {
                e = e || window.event;
                var target = e.target || e.srcElement;
                if (target === box || (target && target.id === 'md-lb-frame')) { md.close(); }
                return false;
            };
        }

        var close = el('md-lb-close');
        if (close) { close.onclick = function () { md.close(); return false; }; }

        var prev = el('md-lb-prev');
        if (prev) { prev.onclick = function () { md.step(-1); return false; }; }

        var next = el('md-lb-next');
        if (next) { next.onclick = function () { md.step(1); return false; }; }

        var zoomIn = el('md-lb-zoomin');
        if (zoomIn) { zoomIn.onclick = function () { md.zoomBy(1.25); return false; }; }

        var zoomOut = el('md-lb-zoomout');
        if (zoomOut) { zoomOut.onclick = function () { md.zoomBy(0.8); return false; }; }

        var zoomReset = el('md-lb-zoom');
        if (zoomReset) {
            zoomReset.onclick = function () {
                md.setZoom(1); md.panX = 0; md.panY = 0; md.render();
                return false;
            };
        }

        var view = el('md-lb-img');
        if (view) {
            view.onmousedown = function (e) {
                e = e || window.event;
                md.dragging = true;
                md.startX = e.clientX;
                md.startY = e.clientY;
                md.baseX = md.panX;
                md.baseY = md.panY;
                return false;
            };
            view.onmousemove = function (e) {
                if (!md.dragging) { return true; }
                e = e || window.event;
                md.panX = md.baseX + (e.clientX - md.startX);
                md.panY = md.baseY + (e.clientY - md.startY);
                md.render();
                return false;
            };
            view.onmouseup = function () { md.dragging = false; return false; };
            view.onmousewheel = function (e) {
                e = e || window.event;
                md.zoomBy(e.wheelDelta > 0 ? 1.15 : 0.87);
                return false;
            };
        }
    }

    if (document.readyState === 'complete') { wire(); }
    else { window.onload = wire; }
})();
";
    }
}
