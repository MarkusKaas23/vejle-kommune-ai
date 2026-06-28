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

    // Mount inside umb-backoffice (light DOM, no shadow root in Umbraco 17).
    // This is critical: umb-icon uses Umbraco's context API which bubbles up
    // through the DOM tree. Mounting inside umb-backoffice means our umb-icon
    // elements share the same context providers as the rest of the backoffice.
    // Mounting at document.body puts us outside the context tree → umb-icon renders nothing.
    _ensureStyles();
    const iconWrapper = document.createElement('uui-icon-registry-essential');
    iconWrapper.id = 'vejle-icon-wrapper';
    iconWrapper.appendChild(overlay);
    const _mountTarget = document.querySelector('umb-backoffice') ?? document.body;
    _mountTarget.appendChild(iconWrapper);

    // Bubble-phase logger: runs AFTER uui-icon-registry-essential has had a chance to resolve.
    // Any icon still unresolved here is a candidate to add to _ICON_SVG_MAP.
    iconWrapper.addEventListener('uui-icon-request', (e) => {
        if (!e.svgString) {
            console.warn('[VejleAi] UNRESOLVED icon:', e.iconName);
        }
    });

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
 * Uses <uui-icon> via uui-icon-registry-essential for resolved icons.
 * Falls back to a colored initial-letter badge for unresolved icons.
 */
function _iconHtml(iconName, cssColor, size) {
    // Check inline SVG map first (for Umbraco-specific icons not in uui-icon-registry-essential)
    const inlineSvg = _ICON_SVG_MAP[iconName];
    if (inlineSvg) {
        const col = cssColor || 'currentColor';
        return `<span style="color:${col};width:${size}px;height:${size}px;display:inline-flex;flex-shrink:0;vertical-align:middle;align-items:center;justify-content:center;">${inlineSvg}</span>`;
    }
    return `<uui-icon name="${esc(iconName)}" style="color:${cssColor};width:${size}px;height:${size}px;display:inline-flex;flex-shrink:0;vertical-align:middle;"></uui-icon>`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Inline SVG map — Umbraco-specific icons not in uui-icon-registry-essential
// Add entries here as needed (names are WITHOUT the "icon-" prefix).
// Run this in the browser console to find which names are missing:
//   [...document.querySelectorAll('uui-icon')].forEach(el => console.log(el.name, el.shadowRoot?.querySelector('svg') ? '✓' : '✗'))
// ─────────────────────────────────────────────────────────────────────────────
const _ICON_SVG_MAP = {
    'newspaper': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M496 128v16a8 8 0 0 1-8 8h-24v12c0 6.627-5.373 12-12 12H60c-6.627 0-12-5.373-12-12V48c0-6.627 5.373-12 12-12h340c6.627 0 12 5.373 12 12v68h24a8 8 0 0 1 8 8zm-60 198H76c-6.627 0-12 5.373-12 12v12c0 6.627 5.373 12 12 12h360c6.627 0 12-5.373 12-12v-12c0-6.627-5.373-12-12-12zm0-96H76c-6.627 0-12 5.373-12 12v12c0 6.627 5.373 12 12 12h360c6.627 0 12-5.373 12-12v-12c0-6.627-5.373-12-12-12zm0 192H76c-6.627 0-12 5.373-12 12v12c0 6.627 5.373 12 12 12h360c6.627 0 12-5.373 12-12v-12c0-6.627-5.373-12-12-12z"/></svg>`,
    'home': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 576 512" fill="currentColor" width="100%" height="100%"><path d="M280.37 148.26L96 300.11V464a16 16 0 0 0 16 16l112.06-.29a16 16 0 0 0 15.92-16V368a16 16 0 0 1 16-16h64a16 16 0 0 1 16 16v95.64a16 16 0 0 0 16 16.05L464 480a16 16 0 0 0 16-16V300L295.67 148.26a12.19 12.19 0 0 0-15.3 0zM571.6 251.47L488 182.56V44.05a12 12 0 0 0-12-12h-56a12 12 0 0 0-12 12v72.61L318.47 43a48 48 0 0 0-61 0L4.34 251.47a12 12 0 0 0-1.6 16.9l25.5 31A12 12 0 0 0 45.15 301l235.22-193.74a12.19 12.19 0 0 1 15.3 0L530.9 301a12 12 0 0 0 16.9-1.6l25.5-31a12 12 0 0 0-1.7-16.93z"/></svg>`,
    'article': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 384 512" fill="currentColor" width="100%" height="100%"><path d="M224 136V0H24C10.7 0 0 10.7 0 24v464c0 13.3 10.7 24 24 24h336c13.3 0 24-10.7 24-24V160H248c-13.2 0-24-10.8-24-24zm64 236c0 6.6-5.4 12-12 12H76c-6.6 0-12-5.4-12-12v-8c0-6.6 5.4-12 12-12h200c6.6 0 12 5.4 12 12v8zm0-96c0 6.6-5.4 12-12 12H76c-6.6 0-12-5.4-12-12v-8c0-6.6 5.4-12 12-12h200c6.6 0 12 5.4 12 12v8zm0-96c0 6.6-5.4 12-12 12H76c-6.6 0-12-5.4-12-12v-8c0-6.6 5.4-12 12-12h200c6.6 0 12 5.4 12 12v8zm96-114.1V24c0-13.3-10.7-24-24-24H248v136h136c0-4.6-1.8-9.2-5.3-12.6z"/></svg>`,
    'list-alt': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M464 480H48c-26.51 0-48-21.49-48-48V80c0-26.51 21.49-48 48-48h416c26.51 0 48 21.49 48 48v352c0 26.51-21.49 48-48 48zM128 120c-22.091 0-40 17.909-40 40s17.909 40 40 40 40-17.909 40-40-17.909-40-40-40zm0 96c-22.091 0-40 17.909-40 40s17.909 40 40 40 40-17.909 40-40-17.909-40-40-40zm0 96c-22.091 0-40 17.909-40 40s17.909 40 40 40 40-17.909 40-40-17.909-40-40-40zm288-136v-32c0-6.627-5.373-12-12-12H204c-6.627 0-12 5.373-12 12v32c0 6.627 5.373 12 12 12h200c6.627 0 12-5.373 12-12zm0 96v-32c0-6.627-5.373-12-12-12H204c-6.627 0-12 5.373-12 12v32c0 6.627 5.373 12 12 12h200c6.627 0 12-5.373 12-12zm0 96v-32c0-6.627-5.373-12-12-12H204c-6.627 0-12 5.373-12 12v32c0 6.627 5.373 12 12 12h200c6.627 0 12-5.373 12-12z"/></svg>`,
    'folder': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M464 128H272l-64-64H48C21.49 64 0 85.49 0 112v288c0 26.51 21.49 48 48 48h416c26.51 0 48-21.49 48-48V176c0-26.51-21.49-48-48-48z"/></svg>`,
    'picture': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M464 448H48c-26.51 0-48-21.49-48-48V112c0-26.51 21.49-48 48-48h416c26.51 0 48 21.49 48 48v288c0 26.51-21.49 48-48 48zM112 120c-30.928 0-56 25.072-56 56s25.072 56 56 56 56-25.072 56-56-25.072-56-56-56zM64 384h384v-112l-87.515-87.515c-4.686-4.686-12.284-4.686-16.971 0L208 320l-55.515-55.515c-4.686-4.686-12.284-4.686-16.971 0L64 336v48z"/></svg>`,
    'accessibility': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M256 48c114.953 0 208 93.029 208 208 0 114.953-93.029 208-208 208-114.953 0-208-93.029-208-208 0-114.953 93.029-208 208-208m0-40C119.033 8 8 119.033 8 256s111.033 248 248 248 248-111.033 248-248S392.967 8 256 8zm0 56C149.961 64 64 149.961 64 256s85.961 192 192 192 192-85.961 192-192S362.039 64 256 64zm0 44c19.882 0 36 16.118 36 36s-16.118 36-36 36-36-16.118-36-36 16.118-36 36-36zm117.741 98.023c-28.374 6.007-56.689 11.646-82.741 13.769V280l21.427 128.899A12 12 0 0 1 300.655 424h-3.881a12 12 0 0 1-11.832-10.065L271 336h-30l-13.942 77.935A12 12 0 0 1 215.226 424h-3.881a12 12 0 0 1-11.973-13.101L221 280v-58.208c-26.052-2.123-54.367-7.762-82.741-13.769-8.104-1.717-13.259-9.71-11.542-17.814 1.717-8.104 9.71-13.259 17.814-11.542C181.658 187.887 218.832 192 256 192c37.168 0 74.342-4.113 111.469-12.123 8.104-1.717 16.097 3.438 17.814 11.542 1.716 8.104-3.439 16.097-11.542 17.814z"/></svg>`,
    'tag': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M0 252.118V48C0 21.49 21.49 0 48 0h204.118a48 48 0 0 1 33.941 14.059l211.882 211.882c18.745 18.745 18.745 49.137 0 67.882L293.823 497.941c-18.745 18.745-49.137 18.745-67.882 0L14.059 286.059A48 48 0 0 1 0 252.118zM112 64c-26.51 0-48 21.49-48 48s21.49 48 48 48 48-21.49 48-48-21.49-48-48-48z"/></svg>`,
    'link': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M326.612 185.391c59.747 59.809 58.927 155.698.36 214.59-.11.12-.24.25-.36.37l-67.2 67.2c-59.27 59.27-155.699 59.262-214.96 0-59.27-59.26-59.27-155.7 0-214.96l37.106-37.106c9.84-9.84 26.786-3.3 27.294 10.606.648 17.722 3.826 35.527 9.69 52.721 1.986 5.822.567 12.262-3.783 16.612l-13.087 13.087c-28.026 28.026-28.905 73.66-1.155 101.96 28.024 28.579 74.086 28.749 102.325.51l67.2-67.19c28.191-28.191 28.073-73.757 0-101.83-3.701-3.694-7.429-6.564-10.341-8.569a16 16 0 0 1-6.947-12.606c-.396-10.567 3.348-21.456 11.698-29.806l21.054-21.055c5.521-5.521 14.182-6.199 20.584-1.731a152.482 152.482 0 0 1 20.522 17.197zM467.547 44.449c-59.261-59.262-155.69-59.27-214.96 0l-67.2 67.2c-.12.12-.25.25-.36.37-58.566 58.892-59.387 154.781.36 214.59a152.454 152.454 0 0 0 20.521 17.196c6.402 4.468 15.064 3.789 20.584-1.731l21.054-21.055c8.35-8.35 12.094-19.239 11.698-29.806a16 16 0 0 0-6.947-12.606c-2.912-2.005-6.64-4.875-10.341-8.569-28.073-28.073-28.191-73.639 0-101.83l67.2-67.19c28.239-28.239 74.3-28.069 102.325.51 27.75 28.3 26.872 73.934-1.155 101.96l-13.087 13.087c-4.35 4.35-5.769 10.79-3.783 16.612 5.864 17.194 9.042 34.999 9.69 52.721.509 13.906 17.454 20.446 27.294 10.606l37.106-37.106c59.271-59.259 59.271-155.699.001-214.959z"/></svg>`,
    'user': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 448 512" fill="currentColor" width="100%" height="100%"><path d="M224 256c70.7 0 128-57.3 128-128S294.7 0 224 0 96 57.3 96 128s57.3 128 128 128zm89.6 32h-16.7c-22.2 10.2-46.9 16-72.9 16s-50.6-5.8-72.9-16h-16.7C60.2 288 0 348.2 0 422.4V464c0 26.5 21.5 48 48 48h352c26.5 0 48-21.5 48-48v-41.6c0-74.2-60.2-134.4-134.4-134.4z"/></svg>`,
    'users': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 512" fill="currentColor" width="100%" height="100%"><path d="M96 224c35.3 0 64-28.7 64-64s-28.7-64-64-64-64 28.7-64 64 28.7 64 64 64zm448 0c35.3 0 64-28.7 64-64s-28.7-64-64-64-64 28.7-64 64 28.7 64 64 64zm32 32h-64c-17.6 0-33.5 7.1-45.1 18.6 40.3 22.1 68.9 62 75.1 109.4h66c17.7 0 32-14.3 32-32v-32c0-35.3-28.7-64-64-64zm-256 0c61.9 0 112-50.1 112-112S381.9 32 320 32 208 82.1 208 144s50.1 112 112 112zm76.8 32h-8.3c-20.8 10-43.9 16-68.5 16s-47.6-6-68.5-16h-8.3C179.6 288 128 339.6 128 403.2V432c0 26.5 21.5 48 48 48h288c26.5 0 48-21.5 48-48v-28.8c0-63.6-51.6-115.2-115.2-115.2zm-223.7-13.4C161.5 263.1 145.6 256 128 256H64c-35.3 0-64 28.7-64 64v32c0 17.7 14.3 32 32 32h65.9c6.3-47.4 34.9-87.3 75.2-109.4z"/></svg>`,
    'globe': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 496 512" fill="currentColor" width="100%" height="100%"><path d="M336.5 160C322 70.7 287.8 8 248 8s-74 62.7-88.5 152h177zM152 256c0 22.2 1.2 43.5 3.3 64h185.3c2.1-20.5 3.3-41.8 3.3-64s-1.2-43.5-3.3-64H155.3c-2.1 20.5-3.3 41.8-3.3 64zm324.7-96c-28.6-67.9-86.5-120.4-158-141.6 24.4 33.8 41.2 84.7 50 141.6h108zM177.2 18.4C105.8 39.6 47.8 92.1 19.3 160h108c8.7-56.9 25.5-107.8 49.9-141.6zM487.4 192H372.7c2.1 21 3.3 42.5 3.3 64s-1.2 43-3.3 64h114.6c5.5-20.5 8.6-41.8 8.6-64s-3.1-43.5-8.5-64zM120 256c0-21.5 1.2-43 3.3-64H8.6C3.2 212.5 0 233.8 0 256s3.2 43.5 8.6 64h114.6c-2-21-3.2-42.5-3.2-64zm39.5 96c14.5 89.3 48.7 152 88.5 152s74-62.7 88.5-152h-177zm159.3 141.6c71.4-21.2 129.4-73.7 158-141.6h-108c-8.8 56.9-25.6 107.8-50 141.6zM19.3 352c28.6 67.9 86.5 120.4 158 141.6-24.4-33.8-41.2-84.7-50-141.6h-108z"/></svg>`,
    'eye': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 576 512" fill="currentColor" width="100%" height="100%"><path d="M572.52 241.4C518.29 135.59 410.93 64 288 64S57.68 135.64 3.48 241.41a32.35 32.35 0 0 0 0 29.19C57.71 376.41 165.07 448 288 448s230.32-71.64 284.52-177.41a32.35 32.35 0 0 0 0-29.19zM288 400a144 144 0 1 1 144-144 143.93 143.93 0 0 1-144 144zm0-240a95.31 95.31 0 0 0-25.31 3.79 47.85 47.85 0 0 1-66.9 66.9A95.78 95.78 0 1 0 288 160z"/></svg>`,
    'list': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M80 368H16a16 16 0 0 0-16 16v64a16 16 0 0 0 16 16h64a16 16 0 0 0 16-16v-64a16 16 0 0 0-16-16zm0-320H16A16 16 0 0 0 0 64v64a16 16 0 0 0 16 16h64a16 16 0 0 0 16-16V64a16 16 0 0 0-16-16zm0 160H16a16 16 0 0 0-16 16v64a16 16 0 0 0 16 16h64a16 16 0 0 0 16-16v-64a16 16 0 0 0-16-16zm416 176H176a16 16 0 0 0-16 16v32a16 16 0 0 0 16 16h320a16 16 0 0 0 16-16v-32a16 16 0 0 0-16-16zm0-320H176a16 16 0 0 0-16 16v32a16 16 0 0 0 16 16h320a16 16 0 0 0 16-16V80a16 16 0 0 0-16-16zm0 160H176a16 16 0 0 0-16 16v32a16 16 0 0 0 16 16h320a16 16 0 0 0 16-16v-32a16 16 0 0 0-16-16z"/></svg>`,
    'document-dashed': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 384 512" fill="currentColor" width="100%" height="100%"><path d="M369.9 97.9L286 14C277 5 264.8-.1 252.1-.1H48C21.5 0 0 21.5 0 48v416c0 26.5 21.5 48 48 48h288c26.5 0 48-21.5 48-48V131.9c0-12.7-5.1-25-14.1-34zM332.1 128H256V51.9l76.1 76.1zM48 464V48h160v104c0 13.3 10.7 24 24 24h104v288H48z"/></svg>`,
    'settings': `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="currentColor" width="100%" height="100%"><path d="M487.4 315.7l-42.6-24.6c4.3-23.2 4.3-47 0-70.2l42.6-24.6c4.9-2.8 7.1-8.6 5.5-14-11.1-35.6-30-67.8-54.7-94.6-3.8-4.1-10-5.1-14.8-2.3l-42.6 24.6c-17.9-15.4-38.5-27.3-60.8-35.1V25.8c0-5.7-4.1-10.6-9.8-11.7-36.3-6.8-73.8-6.9-110.5 0-5.6 1-9.8 6-9.8 11.7v49.2c-22.3 7.8-42.8 19.8-60.8 35.1L88.9 85.5c-4.9-2.8-11-1.9-14.8 2.3-24.7 26.7-43.6 58.9-54.7 94.6-1.7 5.4.6 11.2 5.5 14l42.6 24.6c-4.3 23.2-4.3 47 0 70.2L24.9 315.7c-4.9 2.8-7.1 8.6-5.5 14 11.1 35.6 30 67.8 54.7 94.6 3.8 4.1 10 5.1 14.8 2.3l42.6-24.6c17.9 15.4 38.5 27.3 60.8 35.1v49.2c0 5.7 4.1 10.6 9.8 11.7 36.3 6.8 73.8 6.9 110.5 0 5.6-1 9.8-6 9.8-11.7v-49.2c22.3-7.8 42.8-19.8 60.8-35.1l42.6 24.6c4.9 2.8 11 1.9 14.8-2.3 24.7-26.7 43.6-58.9 54.7-94.6 1.5-5.4-.7-11.2-5.6-14zM256 336c-44.1 0-80-35.9-80-80s35.9-80 80-80 80 35.9 80 80-35.9 80-80 80z"/></svg>`,
};

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
