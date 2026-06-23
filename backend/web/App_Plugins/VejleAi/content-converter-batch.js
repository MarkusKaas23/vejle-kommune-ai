/**
 * Vejle AI — Batch Content Type Converter
 * entityBulkAction: zero Umbraco-modal dependencies, pure DOM overlay.
 */

console.log('[VejleAi] content-converter-batch.js loaded');

class VejleBatchConvertAction {
    async execute() {
        console.log('[VejleAi] execute() called, selection:', this.selection);
        try {
            await _showDialog(Array.from(this.selection ?? []));
        } catch (err) {
            console.error('[VejleAi] execute() threw:', err);
            alert('[VejleAi] Batch convert error:\n' + err);
        }
    }
}

export default VejleBatchConvertAction;

// ─────────────────────────────────────────────────────────────────────────────
// Main dialog
// ─────────────────────────────────────────────────────────────────────────────

async function _showDialog(selection) {
    const overlay = document.createElement('div');
    overlay.id = 'vejle-batch-overlay';
    overlay.innerHTML = `
        <div id="vejle-batch-dialog">
            <div id="vejle-batch-header">
                <h2>Convert content type</h2>
                <button id="vejle-batch-close">✕</button>
            </div>
            <div id="vejle-batch-body">
                <div class="vb-info">
                    <strong>${selection.length}</strong>
                    node${selection.length !== 1 ? 's' : ''} selected
                </div>
                <div class="vb-field">
                    <label>Convert to document type</label>
                    <div id="vejle-picker-wrap"></div>
                </div>
                <div class="vb-field">
                    <label>Target location</label>
                    <div class="vb-placement-mode">
                        <label class="vb-radio-label">
                            <input type="radio" name="vejle-placement" value="source" checked>
                            Place next to originals <span class="vb-radio-hint">(same parent as each source node)</span>
                        </label>
                        <label class="vb-radio-label">
                            <input type="radio" name="vejle-placement" value="custom">
                            Move to a specific location
                        </label>
                    </div>
                    <div id="vejle-target-wrap" style="display:none;margin-top:8px;"></div>
                </div>
                <div style="margin-bottom:20px;">
                    <button id="vejle-compare-btn" class="vb-btn vb-btn-primary" disabled>Compare →</button>
                </div>
                <div id="vejle-error" class="vb-error" style="display:none;"></div>
                <div id="vejle-result"></div>
            </div>
        </div>
    `;

    // Wrap in uui-icon-registry-essential for the basic ~50 UUI icons.
    // Then intercept any unresolved uui-icon-request events and proxy them
    // to Umbraco's live umb-icon-registry (deep in shadow DOM) to get the
    // full backoffice icon set. This avoids any shadow-DOM mounting complexity.
    _ensureStyles();
    const iconWrapper = document.createElement('uui-icon-registry-essential');
    iconWrapper.id = 'vejle-icon-wrapper';
    iconWrapper.appendChild(overlay);
    document.body.appendChild(iconWrapper);

    // Proxy icon requests to Umbraco's real registry using CAPTURE phase so
    // we run before uui-icon-registry-essential's bubble listener (and before
    // any stopPropagation it might call).
    // Debug: find icon-related elements from document level (umb-backoffice has no shadow root in Umbraco 17)
    (function debugIconTree() {
        function scan(root, depth, path) {
            if (depth > 4) return;
            for (const el of root.querySelectorAll('*')) {
                const tag = el.tagName.toLowerCase();
                if (tag.includes('icon') || tag.includes('registry')) {
                    console.log('[VejleAi] found:', path + ' > ' + tag, '| shadowRoot:', !!el.shadowRoot);
                }
                if (el.shadowRoot) scan(el.shadowRoot, depth + 1, path + '>' + tag + '#SR');
            }
        }
        scan(document, 0, 'document');
    })();
    const _cachedReg = _findUmbIconRegistry();
    console.log('[VejleAi] umb-icon-registry found:', _cachedReg?.tagName ?? 'NOT FOUND');
    iconWrapper.addEventListener('uui-icon-request', (e) => {
        if (e.svgString) return; // already resolved
        if (!_cachedReg) return;
        const temp = document.createElement('span');
        _cachedReg.appendChild(temp);
        const proxy = new Event('uui-icon-request', { bubbles: true, composed: true });
        proxy.iconName = e.iconName;
        temp.dispatchEvent(proxy);
        temp.remove();
        if (proxy.svgString) e.svgString = proxy.svgString;
    }, true); // capture phase

    // DOM refs
    const pickerWrap = overlay.querySelector('#vejle-picker-wrap');
    const compareBtn = overlay.querySelector('#vejle-compare-btn');
    const errorEl    = overlay.querySelector('#vejle-error');
    const resultEl   = overlay.querySelector('#vejle-result');

    // ── Close ────────────────────────────────────────────────────────────────
    // Use pointer events so resize drags don't accidentally trigger close.
    // Only close when the pointer went DOWN on the overlay backdrop AND
    // moved less than 6px before release (genuine click, not a drag).
    const close = () => iconWrapper.remove();
    overlay.querySelector('#vejle-batch-close').addEventListener('click', close);

    let _ptrOrigin = null;
    overlay.addEventListener('pointerdown', (e) => {
        _ptrOrigin = e.target === overlay ? { x: e.clientX, y: e.clientY } : null;
    });
    overlay.addEventListener('pointerup', (e) => {
        if (!_ptrOrigin || e.target !== overlay) { _ptrOrigin = null; return; }
        const moved = Math.hypot(e.clientX - _ptrOrigin.x, e.clientY - _ptrOrigin.y);
        _ptrOrigin = null;
        if (moved < 6) close();
    });

    // ── Fetch & build picker ─────────────────────────────────────────────────
    let selectedAlias = '';
    pickerWrap.innerHTML = `<div class="vb-picker-loading">Loading document types…</div>`;

    const docTypes = await _fetchDocumentTypes();

    if (!docTypes) {
        pickerWrap.innerHTML = `<input id="vejle-alias-input" class="vb-alias-input" type="text" placeholder="e.g. newsArticle">`;
        const inp = pickerWrap.querySelector('#vejle-alias-input');
        inp.addEventListener('input', () => { selectedAlias = inp.value.trim(); compareBtn.disabled = !selectedAlias; });
        inp.addEventListener('keydown', (e) => { if (e.key === 'Enter' && !compareBtn.disabled) compareBtn.click(); });
        inp.focus();
    } else {
        _buildPicker(pickerWrap, docTypes, (alias) => {
            selectedAlias = alias;
            compareBtn.disabled = !selectedAlias;
        });
    }

    // ── Placement mode ────────────────────────────────────────────────────────
    let targetParentKey = null;   // null = UseSourceParent
    const targetWrap = overlay.querySelector('#vejle-target-wrap');

    overlay.querySelectorAll('input[name="vejle-placement"]').forEach(radio => {
        radio.addEventListener('change', () => {
            const custom = radio.value === 'custom' && radio.checked;
            targetWrap.style.display = custom ? '' : 'none';
            if (!custom) {
                targetParentKey = null;
                targetWrap.innerHTML = '';
            } else {
                _buildContentPicker(targetWrap, (key, name) => { targetParentKey = key; });
            }
        });
    });

    // ── Compare ──────────────────────────────────────────────────────────────
    compareBtn.addEventListener('click', async () => {
        if (!selectedAlias) return;
        compareBtn.disabled = true;
        compareBtn.textContent = 'Comparing…';
        errorEl.style.display = 'none';
        resultEl.innerHTML = '';
        try {
            const url = `/vejle/api/content-converter/batch-compare`
                + `?sourceIds=${encodeURIComponent(selection.join(','))}`
                + `&targetAlias=${encodeURIComponent(selectedAlias)}`
                + `&culture=da-DK`;
            const resp = await fetch(url);
            if (!resp.ok) { const b = await resp.json().catch(()=>({})); throw new Error(b?.error ?? `Compare failed (${resp.status})`); }
            renderComparison(await resp.json(), selectedAlias);
        } catch (err) {
            errorEl.textContent = String(err);
            errorEl.style.display = 'block';
        } finally {
            compareBtn.disabled = false;
            compareBtn.textContent = 'Compare →';
        }
    });

    function renderComparison(c, alias) {
        const targetName = c.targetDocumentType?.name || alias;
        const nodeCount  = c.sourceNodes.length;
        const multiNode  = nodeCount > 1;

        // ── Placement info ───────────────────────────────────────────────────
        // Show where each node will land, and flag any tree restriction conflicts.
        const warnings = c.placementWarnings ?? [];
        const usingCustomTarget = targetParentKey != null;

        const placementRows = c.sourceNodes.map(n => {
            const dest = usingCustomTarget
                ? `→ custom location`
                : `→ <em>${esc(n.parentName ?? '(root)')}</em>`;
            const hasConflict = !usingCustomTarget && warnings.some(w => w.nodeKey === n.nodeKey);
            return `<div class="vb-placement-node-row${hasConflict ? ' conflict' : ''}">
                <span class="vb-placement-node-name">${esc(n.nodeName)}</span>
                <span class="vb-placement-node-dest">${dest}${hasConflict ? ' ⚠ not allowed' : ''}</span>
            </div>`;
        }).join('');

        const warningHtml = `
            <div class="vb-placement-overview">
                <div class="vb-placement-overview-title">Placement preview</div>
                ${placementRows}
                ${warnings.length > 0 && !usingCustomTarget ? `
                    <div class="vb-placement-conflict-note">
                        ⚠ ${warnings.length} node${warnings.length !== 1 ? 's' : ''} will be converted
                        but the target type is not listed as an allowed child at that location.
                        Consider using "Move to a specific location" or moving the nodes after conversion.
                    </div>` : ''}
            </div>`;

        // ── Property table helper ────────────────────────────────────────────
        // showHasData: true for per-node tabs (adds a "Has data" column)
        function tableHtml(props, matched, typeMismatch, unmatched, showHasData) {
            const pills = [
                `<span class="vb-pill vb-pill-match">${matched} will be copied</span>`,
                typeMismatch > 0 ? `<span class="vb-pill vb-pill-mismatch">${typeMismatch} type mismatch (skipped)</span>` : '',
                unmatched    > 0 ? `<span class="vb-pill vb-pill-lost">${unmatched} unmatched (lost)</span>` : '',
            ].join('');
            const rows = props.map(p => {
                const cls  = p.status === 'Matched' ? 'row-matched' : p.status === 'TypeMismatch' ? 'row-mismatch' : 'row-unmatched';
                const icon = p.status === 'Matched' ? '✓' : p.status === 'TypeMismatch' ? '⚠' : '✗';
                const src  = (p.sourceEditorAlias ?? '—').replace(/^Umbraco\./, '');
                const tgt  = (p.targetEditorAlias ?? '').replace(/^Umbraco\./, '');
                const hasData = p.sourceValue != null && p.sourceValue !== '';
                const dataCell = showHasData
                    ? (p.status !== 'Matched'
                        ? (hasData
                            ? `<td class="vb-has-data vb-has-data-yes" title="${esc(p.sourceValue)}">● has data</td>`
                            : `<td class="vb-has-data vb-has-data-no">○ empty</td>`)
                        : `<td class="vb-has-data"></td>`)
                    : '';
                return `<tr class="${cls}">
                    <td class="vb-mono">${esc(p.alias)}</td>
                    <td>${icon}</td>
                    <td class="vb-mono">${esc(src)}</td>
                    <td>${tgt ? `<span class="vb-mono">${esc(tgt)}</span>` : '<span class="vb-na">—</span>'}</td>
                    ${dataCell}
                </tr>`;
            }).join('');
            const dataHeader = showHasData ? '<th>Has data</th>' : '';
            return `<div class="vb-pills">${pills}</div>
                <table class="vb-table">
                    <thead><tr>
                        <th>Property</th><th>Status</th>
                        <th>Source editor</th><th>Target editor</th>
                        ${dataHeader}
                    </tr></thead>
                    <tbody>${rows}</tbody>
                </table>`;
        }

        // ── Tab definitions ──────────────────────────────────────────────────
        // When 1 node: no tabs, just the single table.
        // When 2+ nodes: "All (merged)" tab + one tab per node.
        const nodeBds = c.sourceNodeBreakdowns ?? [];
        const tabs = multiNode
            ? [
                { id: 'merged', label: 'All (merged)',
                  html: tableHtml(c.properties, c.matchedCount, c.typeMismatchCount, c.unmatchedCount, false) },
                ...nodeBds.map(nd => ({
                    id:    nd.nodeKey,
                    label: nd.nodeName.length > 24 ? nd.nodeName.slice(0, 22) + '…' : nd.nodeName,
                    html:  tableHtml(nd.properties, nd.matchedCount, nd.typeMismatchCount, nd.unmatchedCount, true),
                })),
              ]
            : [
                { id: 'single', label: null,
                  html: tableHtml(c.properties, c.matchedCount, c.typeMismatchCount, c.unmatchedCount, false) },
              ];

        // Tab bar HTML (only rendered when multiple nodes)
        const tabBarHtml = multiNode
            ? `<div class="vb-tab-bar" role="tablist">
                ${tabs.map((t, i) => `
                    <button class="vb-tab${i === 0 ? ' vb-tab-active' : ''}"
                        data-tab="${esc(t.id)}" role="tab"
                        aria-selected="${i === 0}">${esc(t.label)}</button>`).join('')}
               </div>`
            : '';

        // Pane HTML
        const panesHtml = tabs.map((t, i) =>
            `<div class="vb-tab-pane${i === 0 ? '' : ' vb-tab-pane-hidden'}" data-pane="${esc(t.id)}">${t.html}</div>`
        ).join('');

        // ── Source nodes list ────────────────────────────────────────────────
        const sourceListHtml = nodeCount === 1 ? '' : `
            <details class="vb-source-nodes" ${nodeCount <= 4 ? 'open' : ''}>
                <summary class="vb-source-nodes-summary">
                    ${nodeCount} selected nodes
                </summary>
                <div class="vb-source-nodes-list">
                    ${c.sourceNodes.map(n =>
                        `<div class="vb-source-node-item">
                            <span class="vb-mono" style="font-size:11px;color:#9ca3af;">${esc(n.documentTypeAlias)}</span>
                            <span>${esc(n.nodeName)}</span>
                         </div>`
                    ).join('')}
                </div>
            </details>`;

        resultEl.innerHTML = `
            <div style="margin-top:16px;border-top:2px solid #e5e7eb;padding-top:20px;">
                <h3 style="margin:0 0 12px;font-size:15px;font-weight:600;">
                    ${nodeCount} node${nodeCount !== 1 ? 's' : ''} → ${esc(targetName)}
                </h3>
                ${sourceListHtml}
                ${warningHtml}
                ${tabBarHtml}
                ${panesHtml}
                ${c.matchedCount > 0 ? `
                    <button id="vejle-convert-btn" class="vb-btn vb-btn-success">
                        Convert ${nodeCount} node${nodeCount !== 1 ? 's' : ''} → ${esc(targetName)}
                    </button>` : ''}
            </div>`;

        // Wire tab switching
        if (multiNode) {
            resultEl.querySelectorAll('.vb-tab').forEach(tab => {
                tab.addEventListener('click', () => {
                    const target = tab.dataset.tab;
                    resultEl.querySelectorAll('.vb-tab').forEach(t => {
                        t.classList.toggle('vb-tab-active', t.dataset.tab === target);
                        t.setAttribute('aria-selected', t.dataset.tab === target ? 'true' : 'false');
                    });
                    resultEl.querySelectorAll('.vb-tab-pane').forEach(p => {
                        p.classList.toggle('vb-tab-pane-hidden', p.dataset.pane !== target);
                    });
                });
            });
        }

        const btn = resultEl.querySelector('#vejle-convert-btn');
        if (btn) btn.addEventListener('click', () => runConvert(alias, targetName, nodeCount, btn));
    }

    async function runConvert(alias, targetName, nodeCount, btn) {
        btn.disabled = true;
        btn.textContent = 'Converting…';
        try {
            const resp = await fetch('/vejle/api/content-converter/batch-convert', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sourceIds: selection, targetAlias: alias, culture: 'da-DK', targetParentKey }),
            });
            if (!resp.ok) { const b = await resp.json().catch(()=>({})); throw new Error(b?.error ?? `Convert failed (${resp.status})`); }
            renderResults(await resp.json());
        } catch (err) {
            errorEl.textContent = String(err);
            errorEl.style.display = 'block';
            btn.disabled = false;
            btn.textContent = `Convert ${nodeCount} node${nodeCount !== 1 ? 's' : ''} → ${targetName}`;
        }
    }

    function renderResults(r) {
        const ok   = r.results .map(res => `<div class="vb-result-row success"><span class="vb-result-icon">✓</span><div><strong>${esc(res.newNodeName)}</strong> created as a draft — ${res.propertiesCopied} ${res.propertiesCopied === 1 ? 'property' : 'properties'} copied, ${res.propertiesSkipped} skipped.</div></div>`).join('');
        const fail = r.failures.map(f   => `<div class="vb-result-row failure"><span class="vb-result-icon">✗</span><div><code>${esc(f.sourceKey)}</code> — ${esc(f.error)}</div></div>`).join('');
        resultEl.innerHTML = `
            <div style="margin-top:16px;border-top:2px solid #e5e7eb;padding-top:16px;">
                <h3 style="margin:0 0 12px;font-size:15px;font-weight:600;">Conversion complete</h3>
                ${ok}${fail}
                <button id="vejle-done-btn" class="vb-btn vb-btn-secondary" style="margin-top:16px;">Close</button>
            </div>`;
        resultEl.querySelector('#vejle-done-btn').addEventListener('click', close);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Document type picker
// ─────────────────────────────────────────────────────────────────────────────

async function _fetchDocumentTypes() {
    try {
        const resp = await fetch('/vejle/api/content-converter/document-types');
        if (!resp.ok) throw new Error(`${resp.status}`);
        return await resp.json();
    } catch (err) {
        console.warn('[VejleAi] Could not fetch document types:', err);
        return null;
    }
}

function _parseIcon(iconStr) {
    if (!iconStr) return { name: 'document', color: null };
    const parts = iconStr.split(/\s+/);
    const icon  = parts.find(p => p.startsWith('icon-'));
    const color = parts.find(p => p.startsWith('color-'));
    return {
        name:  icon  ? icon.replace('icon-', '') : 'document',
        color: color ? color.replace('color-', '') : null,
    };
}

const _colorMap = {
    red: '#ef4444', pink: '#ec4899', purple: '#8b5cf6', 'deep-purple': '#7c3aed',
    indigo: '#6366f1', blue: '#3b82f6', 'light-blue': '#0ea5e9', cyan: '#06b6d4',
    teal: '#14b8a6', green: '#22c55e', 'light-green': '#84cc16', lime: '#a3e635',
    yellow: '#eab308', amber: '#f59e0b', orange: '#f97316', 'deep-orange': '#ef4444',
    brown: '#92400e', grey: '#6b7280', 'blue-grey': '#64748b', black: '#1f2937',
};

function _cssColor(iconStr) {
    const { color } = _parseIcon(iconStr);
    return (color && _colorMap[color]) ? _colorMap[color] : '#6b7280';
}

function _buildPicker(container, docTypes, onChange) {
    let isOpen     = false;
    let selectedDt = null;
    let filterText = '';

    container.innerHTML = `
        <div class="vb-picker">
            <div id="vp-trigger" class="vb-picker-trigger" tabindex="0" role="combobox" aria-expanded="false">
                <span id="vp-label" class="vb-picker-placeholder">Select a document type…</span>
                <svg class="vb-picker-arrow" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                    <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clip-rule="evenodd"/>
                </svg>
            </div>
            <div id="vp-panel" class="vb-picker-panel" style="display:none;">
                <input id="vp-search" class="vb-picker-search" type="text"
                    placeholder="Search by name or alias…" autocomplete="off">
                <div id="vp-list" class="vb-picker-list" role="listbox"></div>
            </div>
        </div>
    `;

    const trigger = container.querySelector('#vp-trigger');
    const panel   = container.querySelector('#vp-panel');
    const search  = container.querySelector('#vp-search');
    const list    = container.querySelector('#vp-list');
    const label   = container.querySelector('#vp-label');

    // ── Render list with icon fallback ────────────────────────────────────────
    function renderList() {
        const q = filterText.toLowerCase();
        const filtered = docTypes.filter(dt =>
            dt.name.toLowerCase().includes(q) || dt.alias.toLowerCase().includes(q)
        );
        if (!filtered.length) {
            list.innerHTML = `<div class="vb-picker-empty">No document types match "${esc(filterText)}"</div>`;
            return;
        }
        list.innerHTML = filtered.map(dt => {
            const { name: iconName } = _parseIcon(dt.icon);
            const col = _cssColor(dt.icon);
            const sel = selectedDt?.alias === dt.alias ? ' vb-picker-item-selected' : '';
            return `<div class="vb-picker-item${sel}" data-alias="${esc(dt.alias)}" role="option">
                ${_iconHtml(iconName, col, 18)}
                <span class="vb-picker-name">${esc(dt.name)}</span>
                <span class="vb-picker-alias">${esc(dt.alias)}</span>
            </div>`;
        }).join('');

        // Wire clicks
        list.querySelectorAll('.vb-picker-item').forEach(el => {
            el.addEventListener('click', () => {
                const alias = el.dataset.alias;
                selectedDt = docTypes.find(dt => dt.alias === alias) ?? null;
                filterText = '';
                closePanel();
                if (selectedDt) {
                    const { name: iconName } = _parseIcon(selectedDt.icon);
                    const col = _cssColor(selectedDt.icon);
                    label.innerHTML = `
                        <span class="vb-picker-selected-inner">
                            ${_iconHtml(iconName, col, 16)}
                            <span>${esc(selectedDt.name)}</span>
                            <span class="vb-picker-alias">${esc(selectedDt.alias)}</span>
                        </span>`;
                    label.classList.remove('vb-picker-placeholder');
                    onChange(selectedDt.alias);
                }
            });
        });
    }

    // ── Open / close ──────────────────────────────────────────────────────────
    function _fitPanelHeight() {
        const dialog = document.getElementById('vejle-batch-dialog');
        if (!dialog) return;
        const triggerBottom = trigger.getBoundingClientRect().bottom;
        const dialogBottom  = dialog.getBoundingClientRect().bottom;
        panel.style.maxHeight = Math.max(120, dialogBottom - triggerBottom - 20) + 'px';
    }

    function openPanel() {
        isOpen = true;
        panel.style.display = 'flex';
        trigger.setAttribute('aria-expanded', 'true');
        trigger.classList.add('open');
        filterText = '';
        search.value = '';
        _fitPanelHeight();
        renderList();
        search.focus();
    }

    function closePanel() {
        isOpen = false;
        panel.style.display = 'none';
        trigger.setAttribute('aria-expanded', 'false');
        trigger.classList.remove('open');
    }

    // Refit panel whenever the dialog is resized
    // Use container.closest() so this works whether we're in light or shadow DOM
    const ro = new ResizeObserver(() => { if (isOpen) _fitPanelHeight(); });
    ro.observe(container.closest('#vejle-batch-dialog') ?? document.body);

    trigger.addEventListener('click', () => isOpen ? closePanel() : openPanel());
    trigger.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); isOpen ? closePanel() : openPanel(); }
        if (e.key === 'Escape') closePanel();
    });
    search.addEventListener('input',   () => { filterText = search.value; renderList(); });
    search.addEventListener('keydown', (e) => { if (e.key === 'Escape') closePanel(); });

    // Close picker when clicking outside it (but not on the dialog backdrop)
    document.addEventListener('pointerdown', function outsideClick(e) {
        if (!container.contains(e.target)) {
            closePanel();
            document.removeEventListener('pointerdown', outsideClick);
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Icon resolution — probe Umbraco's own icon registry directly
// ─────────────────────────────────────────────────────────────────────────────

// Cache so we only probe each icon name once per session.
// ─────────────────────────────────────────────────────────────────────────────
// Content node picker (target location)
// ─────────────────────────────────────────────────────────────────────────────

function _buildContentPicker(container, onChange) {
    let selectedNode = null;
    let debounceTimer = null;

    container.innerHTML = `
        <div class="vb-cpicker">
            <input id="vcp-search" class="vb-cpicker-search" type="text"
                placeholder="Search for a content node by name…" autocomplete="off">
            <div id="vcp-list" class="vb-cpicker-list" style="display:none;"></div>
            <div id="vcp-selected" class="vb-cpicker-selected" style="display:none;"></div>
        </div>`;

    const search   = container.querySelector('#vcp-search');
    const list     = container.querySelector('#vcp-list');
    const selected = container.querySelector('#vcp-selected');

    search.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        const q = search.value.trim();
        if (!q) { list.style.display = 'none'; list.innerHTML = ''; return; }
        debounceTimer = setTimeout(() => fetchNodes(q), 250);
    });

    search.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { list.style.display = 'none'; list.innerHTML = ''; }
    });

    async function fetchNodes(q) {
        list.innerHTML = '<div class="vb-cpicker-loading">Searching…</div>';
        list.style.display = 'block';
        try {
            const resp = await fetch(`/vejle/api/content-converter/content-nodes?q=${encodeURIComponent(q)}`);
            if (!resp.ok) throw new Error(resp.status);
            const nodes = await resp.json();
            renderNodes(nodes);
        } catch {
            list.innerHTML = '<div class="vb-cpicker-empty">Search failed.</div>';
        }
    }

    function renderNodes(nodes) {
        if (!nodes.length) {
            list.innerHTML = '<div class="vb-cpicker-empty">No content nodes found.</div>';
            return;
        }
        list.innerHTML = nodes.map(n => {
            const { name: iconName } = _parseIcon(n.icon);
            const col = _cssColor(n.icon);
            // path is raw Umbraco ID path like "-1,1234,5678" — use segment count as depth hint
            const depth = (n.path?.split(',').length ?? 2) - 2;
            const indent = depth > 0 ? `${'·'.repeat(depth)} ` : '';
            return `<div class="vb-cpicker-item" data-key="${esc(n.key)}" data-name="${esc(n.name)}">
                ${_iconHtml(iconName, col, 16)}
                <div class="vb-cpicker-item-info">
                    <span class="vb-cpicker-item-name">${indent}${esc(n.name)}</span>
                    <span class="vb-cpicker-item-path">${esc(n.docTypeAlias)}</span>
                </div>
            </div>`;
        }).join('');
        list.querySelectorAll('.vb-cpicker-item').forEach(el => {
            el.addEventListener('click', () => {
                selectedNode = { key: el.dataset.key, name: el.dataset.name };
                list.style.display = 'none';
                search.value = '';
                selected.innerHTML = `
                    <div class="vb-cpicker-chosen">
                        <span>📁 ${esc(selectedNode.name)}</span>
                        <button class="vb-cpicker-clear" title="Clear">✕</button>
                    </div>`;
                selected.style.display = 'block';
                selected.querySelector('.vb-cpicker-clear').addEventListener('click', () => {
                    selectedNode = null;
                    selected.style.display = 'none';
                    selected.innerHTML = '';
                    onChange(null, null);
                });
                onChange(selectedNode.key, selectedNode.name);
            });
        });
    }

    // Close list when clicking outside
    document.addEventListener('pointerdown', function outside(e) {
        if (!container.contains(e.target)) {
            list.style.display = 'none';
            document.removeEventListener('pointerdown', outside);
        }
    });
}

/**
 * Return an HTML string for a document-type icon.
 *
 * Uses <umb-icon> (Umbraco 17's native icon element) which resolves icons
 * through Umbraco's internal context system — no dependency on umb-icon-registry
 * being reachable from our code. The `name` attribute uses the full "icon-xxx"
 * format as stored in Umbraco's database.
 */
function _iconHtml(iconName, cssColor, size) {
    // umb-icon expects the full "icon-xxx" name with prefix
    const fullName = iconName.startsWith('icon-') ? iconName : `icon-${iconName}`;
    return `<umb-icon name="${esc(fullName)}" style="color:${cssColor};width:${size}px;height:${size}px;display:inline-flex;flex-shrink:0;vertical-align:middle;"></umb-icon>`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function esc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

/**
 * Find Umbraco's live umb-icon-registry by recursing into shadow roots.
 * Searches from document level — umb-backoffice has no shadow root in Umbraco 17
 * so we must check all elements with shadow roots, not just umb-backoffice.
 * Searches only for 'umb-icon-registry' so our own uui-icon-registry-essential
 * wrapper is never returned.
 */
function _findUmbIconRegistry(root = document, depth = 0) {
    if (depth > 6) return null;
    const reg = root.querySelector('umb-icon-registry');
    if (reg) return reg;
    // Check all elements with shadow roots at every depth level
    for (const el of root.querySelectorAll('*')) {
        if (el.shadowRoot) {
            const found = _findUmbIconRegistry(el.shadowRoot, depth + 1);
            if (found) return found;
        }
    }
    return null;
}

function _ensureStyles() {
    if (document.getElementById('vejle-batch-styles')) return;
    const el = document.createElement('style');
    el.id = 'vejle-batch-styles';
    el.textContent = `
/* Overlay & dialog */
#vejle-batch-overlay{position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;}
#vejle-batch-dialog{background:#fff;border-radius:8px;box-shadow:0 24px 80px rgba(0,0,0,.35);width:clamp(480px,60vw,860px);min-width:420px;min-height:280px;max-height:90vh;display:flex;flex-direction:column;overflow:hidden;font-family:system-ui,sans-serif;font-size:14px;resize:both;}
#vejle-batch-header{display:flex;align-items:center;justify-content:space-between;padding:20px 24px 16px;border-bottom:1px solid #e5e7eb;flex-shrink:0;}
#vejle-batch-header h2{margin:0;font-size:17px;font-weight:700;color:#1b264f;}
#vejle-batch-close{background:none;border:none;font-size:18px;cursor:pointer;color:#6b7280;padding:4px 8px;border-radius:4px;line-height:1;}
#vejle-batch-close:hover{background:#f3f4f6;color:#111;}
#vejle-batch-body{padding:20px 24px 24px;overflow-y:auto;flex:1;}
/* Info / field / buttons */
.vb-info{margin-bottom:16px;padding:10px 14px;background:#f5f5f5;border-radius:6px;font-size:13px;}
.vb-field{display:flex;flex-direction:column;gap:5px;margin-bottom:16px;}
.vb-field label{font-weight:600;font-size:13px;}
.vb-alias-input{padding:8px 12px;border:1px solid #d1d5db;border-radius:4px;font-size:14px;font-family:monospace;max-width:480px;}
.vb-alias-input:focus{outline:none;border-color:#3544b1;}
.vb-btn{border:none;padding:8px 18px;border-radius:4px;cursor:pointer;font-size:14px;font-weight:600;}
.vb-btn:disabled{opacity:.5;cursor:default;}
.vb-btn-primary{background:#3544b1;color:#fff;}
.vb-btn-primary:not(:disabled):hover{background:#2a368e;}
.vb-btn-success{background:#059669;color:#fff;}
.vb-btn-success:not(:disabled):hover{background:#047857;}
.vb-btn-secondary{background:#6b7280;color:#fff;}
.vb-btn-secondary:hover{background:#4b5563;}
.vb-error{color:#ef4444;background:#fff5f5;border:1px solid #fca5a5;padding:10px 14px;border-radius:4px;margin:0 0 16px;}
/* Pills / table */
.vb-pills{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px;}
.vb-pill{padding:3px 10px;border-radius:20px;font-size:12px;font-weight:600;}
.vb-pill-match{background:#d1fae5;color:#065f46;}
.vb-pill-mismatch{background:#fef3c7;color:#92400e;}
.vb-pill-lost{background:#fee2e2;color:#991b1b;}
.vb-table{width:100%;border-collapse:collapse;margin-bottom:24px;font-size:13px;}
.vb-table th{text-align:left;padding:8px 12px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.05em;border-bottom:2px solid #e5e7eb;background:#f9fafb;}
.vb-table td{padding:9px 12px;border-bottom:1px solid #f3f4f6;vertical-align:middle;}
.vb-table tr.row-matched td:first-child{border-left:3px solid #10b981;}
.vb-table tr.row-mismatch td:first-child{border-left:3px solid #f59e0b;}
.vb-table tr.row-unmatched td:first-child{border-left:3px solid #ef4444;}
.vb-table tr.row-mismatch{background:#fffbeb;}
.vb-table tr.row-unmatched{background:#fff5f5;}
.vb-mono{font-family:monospace;}
.vb-na{color:#9ca3af;font-style:italic;}
/* Result rows */
.vb-result-row{display:flex;gap:12px;align-items:flex-start;padding:10px 14px;border-radius:6px;margin-bottom:8px;}
.vb-result-row.success{background:#ecfdf5;border:1px solid #6ee7b7;}
.vb-result-row.failure{background:#fff5f5;border:1px solid #fca5a5;}
.vb-result-icon{font-size:18px;line-height:1.4;}
.vb-result-row.success .vb-result-icon{color:#10b981;}
.vb-result-row.failure .vb-result-icon{color:#ef4444;}
/* Document type picker */
.vb-picker{position:relative;max-width:480px;}
.vb-picker-loading{padding:9px 12px;border:1px solid #d1d5db;border-radius:4px;color:#9ca3af;font-size:13px;max-width:480px;}
.vb-picker-trigger{display:flex;align-items:center;justify-content:space-between;padding:8px 12px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;background:#fff;min-height:38px;user-select:none;gap:8px;}
.vb-picker-trigger:hover{border-color:#9ca3af;}
.vb-picker-trigger.open{border-color:#3544b1;box-shadow:0 0 0 2px #e0e3fa;}
.vb-picker-placeholder{color:#9ca3af;font-size:14px;}
.vb-picker-arrow{width:18px;height:18px;flex-shrink:0;color:#6b7280;transition:transform .15s;}
.vb-picker-trigger.open .vb-picker-arrow{transform:rotate(180deg);}
.vb-picker-selected-inner{display:flex;align-items:center;gap:8px;}
.vb-picker-name{font-weight:500;}
.vb-picker-alias{font-size:11px;font-family:monospace;color:#6b7280;background:#f3f4f6;padding:1px 5px;border-radius:3px;flex-shrink:0;}
.vb-picker-panel{position:absolute;top:calc(100% + 4px);left:0;right:0;background:#fff;border:1px solid #d1d5db;border-radius:6px;box-shadow:0 4px 20px rgba(0,0,0,.13);z-index:10;display:flex;flex-direction:column;}
.vb-picker-search{padding:9px 12px;border:none;border-bottom:1px solid #e5e7eb;font-size:14px;outline:none;border-radius:6px 6px 0 0;flex-shrink:0;}
.vb-picker-list{overflow-y:auto;flex:1;}
.vb-picker-item{display:flex;align-items:center;gap:10px;padding:9px 14px;cursor:pointer;font-size:14px;}
.vb-picker-item:hover{background:#f5f7ff;}
.vb-picker-item-selected{background:#eef0fe;}
.vb-picker-empty{padding:16px;text-align:center;color:#9ca3af;font-size:13px;}
.vb-dt-badge{border-radius:3px;flex-shrink:0;display:inline-block;vertical-align:middle;}
/* Tabs */
.vb-tab-bar{display:flex;gap:2px;border-bottom:2px solid #e5e7eb;margin-bottom:16px;flex-wrap:wrap;}
.vb-tab{background:none;border:none;padding:8px 14px;font-size:13px;font-weight:500;color:#6b7280;cursor:pointer;border-bottom:2px solid transparent;margin-bottom:-2px;border-radius:4px 4px 0 0;white-space:nowrap;}
.vb-tab:hover{background:#f9fafb;color:#374151;}
.vb-tab-active{color:#3544b1;border-bottom-color:#3544b1;font-weight:600;}
.vb-tab-pane-hidden{display:none;}
/* Source nodes list */
.vb-source-nodes{border:1px solid #e5e7eb;border-radius:6px;margin-bottom:14px;overflow:hidden;}
.vb-source-nodes-summary{padding:8px 14px;font-size:13px;font-weight:600;cursor:pointer;background:#f9fafb;user-select:none;list-style:none;}
.vb-source-nodes-summary::marker,.vb-source-nodes-summary::-webkit-details-marker{display:none;}
.vb-source-nodes-summary::before{content:'▶ ';font-size:10px;color:#6b7280;}
details.vb-source-nodes[open] .vb-source-nodes-summary::before{content:'▼ ';}
.vb-source-nodes-list{padding:4px 0;}
.vb-source-node-item{display:flex;align-items:center;gap:10px;padding:6px 14px;font-size:13px;border-top:1px solid #f3f4f6;}
/* Placement mode */
.vb-placement-mode{display:flex;flex-direction:column;gap:6px;}
.vb-radio-label{display:flex;align-items:center;gap:8px;font-size:13px;cursor:pointer;}
.vb-radio-hint{color:#9ca3af;font-weight:400;}
/* Placement overview in compare result */
.vb-placement-overview{background:#f8faff;border:1px solid #c7d2fe;border-radius:6px;padding:12px 16px;margin-bottom:16px;}
.vb-placement-overview-title{font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:#4338ca;margin-bottom:8px;}
.vb-placement-node-row{display:flex;align-items:center;justify-content:space-between;padding:5px 0;border-top:1px solid #e0e7ff;font-size:13px;gap:16px;}
.vb-placement-node-row:first-of-type{border-top:none;}
.vb-placement-node-row.conflict{color:#92400e;}
.vb-placement-node-name{font-weight:500;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.vb-placement-node-dest{color:#6b7280;font-size:12px;white-space:nowrap;flex-shrink:0;}
.vb-placement-conflict-note{margin-top:10px;padding-top:8px;border-top:1px solid #fcd34d;background:#fffbeb;border-radius:4px;padding:8px 10px;font-size:12px;color:#92400e;}
/* Has-data column (per-node tabs only) */
.vb-has-data{font-size:11px;white-space:nowrap;}
.vb-has-data-yes{color:#b45309;font-weight:600;}
.vb-has-data-no{color:#9ca3af;}
/* Content node picker */
.vb-cpicker{position:relative;}
.vb-cpicker-search{width:100%;box-sizing:border-box;padding:8px 12px;border:1px solid #d1d5db;border-radius:4px;font-size:13px;outline:none;}
.vb-cpicker-search:focus{border-color:#3544b1;box-shadow:0 0 0 2px #e0e3fa;}
.vb-cpicker-list{position:absolute;left:0;right:0;top:calc(100% + 4px);background:#fff;border:1px solid #d1d5db;border-radius:6px;box-shadow:0 4px 20px rgba(0,0,0,.13);z-index:20;max-height:220px;overflow-y:auto;}
.vb-cpicker-loading,.vb-cpicker-empty{padding:12px 14px;font-size:13px;color:#9ca3af;}
.vb-cpicker-item{display:flex;align-items:center;gap:10px;padding:9px 14px;cursor:pointer;font-size:13px;}
.vb-cpicker-item:hover{background:#f5f7ff;}
.vb-cpicker-item-info{display:flex;flex-direction:column;gap:1px;min-width:0;}
.vb-cpicker-item-name{font-weight:500;}
.vb-cpicker-item-path{font-size:11px;color:#9ca3af;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.vb-cpicker-selected{margin-top:6px;}
.vb-cpicker-chosen{display:flex;align-items:center;justify-content:space-between;padding:7px 12px;background:#f0fdf4;border:1px solid #86efac;border-radius:4px;font-size:13px;font-weight:500;color:#166534;}
.vb-cpicker-clear{background:none;border:none;cursor:pointer;color:#6b7280;font-size:14px;padding:0 4px;}
    `;
    document.head.appendChild(el);
}
