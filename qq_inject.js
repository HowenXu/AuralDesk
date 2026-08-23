(function () {
    if (window.__adInjected) return;
    window.__adInjected = true;

    function postDownload(songmid, songname, singer, rowHtml) {
        if (!songmid) return;
        window.chrome.webview.postMessage(JSON.stringify({
            type: 'qq-download',
            songmid: songmid,
            songname: songname || '',
            singer: singer || '',
            rowHtml: rowHtml || ''
        }));
    }

    // ---------- DOM 歌曲提取 ----------
    function extractFromRow(row) {
        var link = row.querySelector('a[href*="songDetail/"]');
        if (!link) return null;
        var href = link.getAttribute('href') || '';
        var m = href.match(/songDetail\/([A-Za-z0-9]+)/);
        if (!m) return null;
        var mid = m[1];
        var name = (link.getAttribute('title') || '').trim();
        if (!name) {
            // 歌名只取链接的直接文本，排除副标题 span
            var direct = '';
            for (var i = 0; i < link.childNodes.length; i++) {
                if (link.childNodes[i].nodeType === 3) direct += link.childNodes[i].textContent;
            }
            name = (direct || link.textContent || '').trim();
        }
        // 歌手：新版歌单行是 .songlist__artist，部分页面用 singer 类
        var singerEl = row.querySelector('.songlist__artist a, .songlist__artist, ' +
            '[class*="singer"] a, [class*="singer"], [class*="artist"] a, [class*="artist"]');
        var singer = singerEl ? (singerEl.textContent || '').trim() : '';
        if (singer.length > 40) singer = singer.substring(0, 40);
        var rowHtml = row.outerHTML ? row.outerHTML.substring(0, 1500) : '';
        return { mid: mid, name: name, singer: singer, rowHtml: rowHtml };
    }

    function scanAllSongs() {
        var out = [], seen = {};
        var rows = document.querySelectorAll('.songlist__item, [class*="songlist__item"], [class*="song_item"]');
        for (var i = 0; i < rows.length; i++) {
            var info = extractFromRow(rows[i]);
            if (info && info.mid && !seen[info.mid]) { seen[info.mid] = 1; out.push(info); }
        }
        if (out.length === 0) {
            var links = document.querySelectorAll('a[href*="songDetail/"]');
            for (var j = 0; j < links.length; j++) {
                var h = links[j].getAttribute('href') || '';
                var m2 = h.match(/songDetail\/([A-Za-z0-9]+)/);
                if (m2 && !seen[m2[1]]) {
                    seen[m2[1]] = 1;
                    out.push({ mid: m2[1], name: (links[j].textContent || links[j].getAttribute('title') || '').trim(), singer: '' });
                }
            }
        }
        return out.slice(0, 100);
    }

    // ---------- UI ----------
    var panel, listEl, badge, badgeCount;

    function buildUI() {
        if (panel) return;
        panel = document.createElement('div');
        panel.style.cssText = 'all:initial;position:fixed;right:14px;bottom:64px;z-index:2147483647;' +
            'width:250px;max-height:60vh;overflow:auto;border-radius:10px;' +
            'background:#141418;border:1px solid #31c27c;' +
            'box-shadow:0 8px 30px rgba(0,0,0,.5);font-family:Segoe UI,Arial,sans-serif;' +
            'font-size:12px;color:#fff;display:none;padding:0;margin:0;line-height:1.4;';
        panel.innerHTML = '<div style="padding:10px 12px 4px;font-weight:bold;color:#31c27c;font-size:13px;">' +
            'AuralDesk 下载</div>' +
            '<div id="ad-list" style="padding:4px 6px 10px;font-size:12px;color:#fff;"></div>';

        badge = document.createElement('button');
        badge.id = 'ad-fab';
        badge.type = 'button';
        badge.title = 'AuralDesk 下载面板';
        badge.style.cssText = 'all:initial;position:fixed;right:14px;bottom:14px;z-index:2147483647;' +
            'width:44px;height:44px;border-radius:50%;border:1px solid #31c27c;' +
            'background:#1f7850;color:#fff;font-size:20px;line-height:1;cursor:pointer;' +
            'box-shadow:0 4px 14px rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;';
        badge.textContent = '↓';

        badgeCount = document.createElement('span');
        badgeCount.style.cssText = 'all:initial;position:fixed;right:8px;bottom:52px;z-index:2147483647;' +
            'min-width:16px;height:16px;border-radius:8px;background:#e74c3c;color:#fff;' +
            'font-size:10px;line-height:16px;text-align:center;padding:0 4px;' +
            'font-family:Segoe UI,Arial,sans-serif;display:none;';

        var root = document.documentElement || document;
        root.appendChild(panel);
        root.appendChild(badge);
        root.appendChild(badgeCount);
        listEl = document.getElementById('ad-list');
    }

    function ensureUI() {
        if (!document.documentElement) return;
        if (!panel || !panel.isConnected) buildUI();
    }

    function togglePanel() {
        ensureUI();
        if (panel.style.display === 'none') renderPanel();
        panel.style.display = panel.style.display === 'none' ? 'block' : 'none';
    }

    function renderPanel() {
        ensureUI();
        if (!listEl) return;
        var songs = scanAllSongs();
        if (!songs.length) {
            listEl.innerHTML = '<div style="padding:6px 8px;color:#999;">未找到歌曲行，请打开歌单/搜索页</div>';
            if (badgeCount) badgeCount.style.display = 'none';
            return;
        }
        var html = '';
        for (var i = 0; i < songs.length; i++) {
            var s = songs[i];
            html += '<div style="display:flex;align-items:center;padding:5px 6px;border-radius:6px;margin:2px 0;">' +
                '<div style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#fff;">' +
                (s.name || '未知') + '</div>' +
                '<div style="color:#888;font-size:11px;margin:0 6px;max-width:70px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' +
                (s.singer || '') + '</div>' +
                '<button type="button" data-mid="' + s.mid + '" data-name="' + (s.name || '') + '" ' +
                'data-singer="' + (s.singer || '') + '" ' +
                'style="border:none;background:#31c27c;color:#111;border-radius:5px;padding:3px 8px;' +
                'cursor:pointer;font-size:12px;font-family:Segoe UI,Arial,sans-serif;">下载</button></div>';
        }
        listEl.innerHTML = html;
        if (badgeCount) {
            badgeCount.textContent = String(songs.length);
            badgeCount.style.display = 'block';
        }
        var btns = listEl.querySelectorAll('button[data-mid]');
        for (var k = 0; k < btns.length; k++) {
            (function (btn) {
                btn.onclick = function () {
                    postDownload(btn.getAttribute('data-mid'), btn.getAttribute('data-name') || '',
                        btn.getAttribute('data-singer') || '', '');
                };
            })(btns[k]);
        }
    }

    function bindBadge() {
        ensureUI();
        if (!badge) return;
        badge.onclick = function (e) {
            if (e) { e.preventDefault(); e.stopPropagation(); }
            togglePanel();
        };
        if (!window.__adDocBound) {
            window.__adDocBound = true;
            document.addEventListener('pointerdown', function (e) {
                if (panel && panel.style.display !== 'none'
                    && !panel.contains(e.target)
                    && e.target !== badge && e.target !== badgeCount) {
                    panel.style.display = 'none';
                }
            }, true);
        }
    }

    // ---------- 行内按钮 ----------
    function addRowButton(row) {
        if (!row || row.querySelector('.ad-row-btn')) return;
        var info = extractFromRow(row);
        if (!info) return;
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'ad-row-btn';
        btn.textContent = '↓';
        btn.title = 'AuralDesk 下载';
        btn.setAttribute('data-mid', info.mid);
        btn.setAttribute('data-name', info.name);
        btn.setAttribute('data-singer', info.singer);
        btn.style.cssText = 'all:initial;margin-left:6px;padding:1px 7px;border-radius:10px;' +
            'border:1px solid #31c27c;background:rgba(49,194,124,.12);color:#31c27c;' +
            'font-size:12px;line-height:15px;cursor:pointer;vertical-align:middle;' +
            'white-space:nowrap;flex-shrink:0;font-family:Segoe UI,Arial,sans-serif;';
        btn.onclick = function (e) {
            if (e) { e.preventDefault(); e.stopPropagation(); }
            postDownload(info.mid, info.name, info.singer, info.rowHtml);
        };
        var nameBox = row.querySelector('.songlist__songname, [class*="songname"]');
        if (nameBox) {
            nameBox.appendChild(btn);
        } else {
            row.appendChild(btn);
        }
    }

    function scanRows() {
        var rows = document.querySelectorAll('.songlist__item, [class*="songlist__item"], [class*="song_item"]');
        for (var i = 0; i < rows.length; i++) addRowButton(rows[i]);
    }

    // ---------- 启动 ----------
    var obs = new MutationObserver(function () {
        ensureUI();
        bindBadge();
        scanRows();
    });
    function start() {
        ensureUI();
        bindBadge();
        scanRows();
        obs.observe(document.documentElement, { childList: true, subtree: true });
        setInterval(function () {
            ensureUI();
            scanRows();
            if (panel && panel.style.display !== 'none') renderPanel();
        }, 2000);
        setTimeout(function () {
            try {
                var sample = document.querySelector('.songlist__item');
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'qq-dom-diagnose',
                    url: location.href,
                    rows: document.querySelectorAll('.songlist__item').length,
                    links: document.querySelectorAll('a[href*="songDetail/"]').length,
                    sample: sample ? sample.outerHTML.substring(0, 2500) : ''
                }));
            } catch (e) { }
        }, 3000);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
