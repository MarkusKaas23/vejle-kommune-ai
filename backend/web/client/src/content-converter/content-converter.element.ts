import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { html, css, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';

// ── API response shapes (mirror the C# sealed records) ─────────────────────

interface PropertyMatchDto {
    alias: string;
    label: string;
    status: 'Matched' | 'TypeMismatch' | 'Unmatched';
    sourceValue?: string;
    sourceEditorAlias: string;
    targetEditorAlias?: string;
}

interface CompareResponse {
    sourceNode: { key: string; name: string; documentTypeAlias: string };
    targetDocumentType: { alias: string; name: string };
    properties: PropertyMatchDto[];
    matchedCount: number;
    typeMismatchCount: number;
    unmatchedCount: number;
}

interface ConvertResponse {
    newNodeKey: string;
    newNodeName: string;
    propertiesCopied: number;
    propertiesSkipped: number;
}

// ── Element ─────────────────────────────────────────────────────────────────

@customElement('vejle-content-converter')
export class ContentConverterElement extends UmbLitElement {

    // Source node key comes from the workspace context.
    @state() private _sourceKey?: string;
    @state() private _sourceName?: string;
    @state() private _sourceTypeAlias?: string;

    // Editor-supplied inputs.
    @state() private _targetAlias = '';
    @state() private _targetParentKey = '';

    // Operation state.
    @state() private _loading = false;
    @state() private _error?: string;
    @state() private _comparison?: CompareResponse;
    @state() private _converted?: ConvertResponse;

    override connectedCallback(): void {
        super.connectedCallback();
        this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (ctx) => {
            this._sourceKey = ctx.getUnique() ?? undefined;
            // Name and type alias are observable streams — read once from the
            // current value using the Umbraco observable pattern.
            ctx.name.getValue !== undefined
                ? (this._sourceName = ctx.name.getValue())
                : void 0;
            ctx.contentTypeAlias?.getValue !== undefined
                ? (this._sourceTypeAlias = ctx.contentTypeAlias.getValue())
                : void 0;
        });
    }

    // ── API calls ────────────────────────────────────────────────────────────

    async #compare(): Promise<void> {
        if (!this._sourceKey || !this._targetAlias.trim()) return;
        this._loading = true;
        this._error = undefined;
        this._comparison = undefined;
        this._converted = undefined;

        try {
            const url = `/umbraco/api/vejle/content-converter/compare?sourceId=${encodeURIComponent(this._sourceKey)}&targetAlias=${encodeURIComponent(this._targetAlias.trim())}`;
            const resp = await fetch(url);
            if (!resp.ok) {
                const body = await resp.json().catch(() => ({}));
                this._error = body?.error ?? `Compare failed (${resp.status})`;
                return;
            }
            this._comparison = await resp.json() as CompareResponse;
        } catch (err) {
            this._error = String(err);
        } finally {
            this._loading = false;
        }
    }

    async #convert(): Promise<void> {
        if (!this._sourceKey || !this._targetAlias.trim()) return;
        this._loading = true;
        this._error = undefined;

        const emptyGuid = '00000000-0000-0000-0000-000000000000';

        try {
            const resp = await fetch('/umbraco/api/vejle/content-converter/convert', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sourceId: this._sourceKey,
                    targetAlias: this._targetAlias.trim(),
                    targetParentKey: this._targetParentKey.trim() || emptyGuid,
                    newNodeName: null,
                    culture: null,
                }),
            });
            if (!resp.ok) {
                const body = await resp.json().catch(() => ({}));
                this._error = body?.error ?? `Convert failed (${resp.status})`;
                return;
            }
            this._converted = await resp.json() as ConvertResponse;
        } catch (err) {
            this._error = String(err);
        } finally {
            this._loading = false;
        }
    }

    // ── Render helpers ───────────────────────────────────────────────────────

    #statusIcon(status: PropertyMatchDto['status']): string {
        return status === 'Matched' ? '✓' : status === 'TypeMismatch' ? '⚠' : '✗';
    }

    #shortAlias(editorAlias: string): string {
        // "Umbraco.TextBox" → "TextBox"
        return editorAlias.replace(/^Umbraco\./, '');
    }

    // ── Template ─────────────────────────────────────────────────────────────

    override render() {
        return html`
            <div class="converter-wrap">
                <h2>Content Type Converter</h2>

                ${this._sourceKey
                    ? html`
                        <div class="source-info">
                            <span class="label">Source node</span>
                            <span class="value">${this._sourceName ?? this._sourceKey}</span>
                            <span class="doctype-chip">${this._sourceTypeAlias ?? '—'}</span>
                        </div>`
                    : html`<p class="warn">Could not read current node from workspace context.</p>`}

                <div class="field-row">
                    <label for="targetAlias">Convert to document type</label>
                    <input
                        id="targetAlias"
                        type="text"
                        placeholder="e.g. veNewsArticle"
                        .value=${this._targetAlias}
                        @input=${(e: Event) => { this._targetAlias = (e.target as HTMLInputElement).value; }}
                    />
                </div>

                <div class="actions">
                    <uui-button
                        look="primary"
                        ?disabled=${this._loading || !this._sourceKey || !this._targetAlias.trim()}
                        @click=${this.#compare}
                    >
                        ${this._loading && !this._comparison ? 'Comparing…' : 'Compare →'}
                    </uui-button>
                </div>

                ${this._error ? html`<p class="error">${this._error}</p>` : nothing}

                ${this._comparison ? this.#renderComparison(this._comparison) : nothing}

                ${this._converted ? this.#renderSuccess(this._converted) : nothing}
            </div>
        `;
    }

    #renderComparison(c: CompareResponse) {
        const canConvert = c.matchedCount > 0;

        return html`
            <div class="comparison">
                <h3>
                    ${c.sourceNode.name}
                    <span class="arrow">→</span>
                    ${c.targetDocumentType.name || c.targetDocumentType.alias}
                </h3>

                <div class="summary-pills">
                    <span class="pill match">${c.matchedCount} copied</span>
                    ${c.typeMismatchCount > 0 ? html`<span class="pill mismatch">${c.typeMismatchCount} type mismatch (skipped)</span>` : nothing}
                    ${c.unmatchedCount > 0 ? html`<span class="pill unmatched">${c.unmatchedCount} unmatched (lost)</span>` : nothing}
                </div>

                <table class="diff-table">
                    <thead>
                        <tr>
                            <th>Property</th>
                            <th>Status</th>
                            <th>Source type</th>
                            <th>Target type</th>
                            <th>Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${c.properties.map(p => html`
                            <tr class="row-${p.status.toLowerCase()}">
                                <td>
                                    <span class="alias">${p.alias}</span>
                                    <span class="prop-label">${p.label !== p.alias ? p.label : ''}</span>
                                </td>
                                <td class="status-cell">
                                    <span class="status-icon">${this.#statusIcon(p.status)}</span>
                                    ${p.status === 'TypeMismatch' ? html`<span class="status-text">type diff</span>` : nothing}
                                    ${p.status === 'Unmatched' ? html`<span class="status-text">not on target</span>` : nothing}
                                </td>
                                <td><code>${this.#shortAlias(p.sourceEditorAlias)}</code></td>
                                <td>${p.targetEditorAlias ? html`<code>${this.#shortAlias(p.targetEditorAlias)}</code>` : html`<span class="na">—</span>`}</td>
                                <td class="value-cell">${p.sourceValue ? html`<span class="preview-val">${p.sourceValue.length > 50 ? p.sourceValue.slice(0, 50) + '…' : p.sourceValue}</span>` : html`<span class="na">empty</span>`}</td>
                            </tr>
                        `)}
                    </tbody>
                </table>

                ${canConvert ? html`
                    <div class="convert-section">
                        <div class="field-row">
                            <label for="parentKey">Place under (parent GUID)</label>
                            <input
                                id="parentKey"
                                type="text"
                                placeholder="Leave empty to place at root"
                                .value=${this._targetParentKey}
                                @input=${(e: Event) => { this._targetParentKey = (e.target as HTMLInputElement).value; }}
                            />
                            <span class="hint">Find the GUID in the Info tab of the target parent node.</span>
                        </div>
                        <div class="actions">
                            <uui-button
                                look="positive"
                                ?disabled=${this._loading}
                                @click=${this.#convert}
                            >
                                ${this._loading ? 'Converting…' : `Convert → ${c.targetDocumentType.name || c.targetDocumentType.alias}`}
                            </uui-button>
                        </div>
                    </div>
                ` : html`<p class="warn">No matched properties — conversion not possible.</p>`}
            </div>
        `;
    }

    #renderSuccess(r: ConvertResponse) {
        return html`
            <div class="success-box">
                <span class="success-icon">✓</span>
                <div>
                    <strong>${r.newNodeName}</strong> created as a draft.
                    <br />
                    ${r.propertiesCopied} ${r.propertiesCopied === 1 ? 'property' : 'properties'} copied,
                    ${r.propertiesSkipped} skipped.
                    <br />
                    <span class="guid-hint">GUID: ${r.newNodeKey}</span>
                </div>
            </div>
        `;
    }

    // ── Styles ───────────────────────────────────────────────────────────────

    static override styles = css`
        :host {
            display: block;
            box-sizing: border-box;
        }

        .converter-wrap {
            padding: 24px;
            max-width: 900px;
            font-family: var(--uui-font-family, sans-serif);
            font-size: 14px;
        }

        h2 {
            margin: 0 0 20px;
            font-size: 18px;
            font-weight: 600;
            color: var(--uui-color-header-contrast, #1b264f);
        }

        h3 {
            margin: 0 0 12px;
            font-size: 15px;
            font-weight: 600;
        }

        .arrow {
            color: var(--uui-color-interactive, #3544b1);
            margin: 0 6px;
        }

        /* Source info badge */
        .source-info {
            display: flex;
            align-items: center;
            gap: 10px;
            margin-bottom: 20px;
            padding: 12px 16px;
            background: var(--uui-color-surface-alt, #f5f5f5);
            border-radius: 6px;
        }

        .label {
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.04em;
            color: var(--uui-color-text-alt, #6b7280);
        }

        .value {
            font-weight: 600;
        }

        .doctype-chip {
            font-size: 11px;
            background: var(--uui-color-surface-emphasis, #e8eaf6);
            color: var(--uui-color-interactive, #3544b1);
            padding: 2px 8px;
            border-radius: 3px;
            font-family: monospace;
        }

        /* Form fields */
        .field-row {
            display: flex;
            flex-direction: column;
            gap: 5px;
            margin-bottom: 16px;
        }

        label {
            font-weight: 600;
            font-size: 13px;
        }

        input[type='text'] {
            padding: 8px 12px;
            border: 1px solid var(--uui-color-border, #d1d5db);
            border-radius: 4px;
            font-size: 14px;
            font-family: monospace;
            max-width: 420px;
            outline: none;
            transition: border-color 0.15s;
        }

        input[type='text']:focus {
            border-color: var(--uui-color-interactive, #3544b1);
        }

        .hint {
            font-size: 12px;
            color: var(--uui-color-text-alt, #6b7280);
        }

        .actions {
            margin-bottom: 20px;
        }

        /* Comparison section */
        .comparison {
            margin-top: 16px;
            border-top: 2px solid var(--uui-color-border, #e5e7eb);
            padding-top: 20px;
        }

        .summary-pills {
            display: flex;
            gap: 8px;
            margin-bottom: 16px;
        }

        .pill {
            padding: 3px 10px;
            border-radius: 20px;
            font-size: 12px;
            font-weight: 600;
        }

        .pill.match    { background: #d1fae5; color: #065f46; }
        .pill.mismatch { background: #fef3c7; color: #92400e; }
        .pill.unmatched { background: #fee2e2; color: #991b1b; }

        /* Diff table */
        .diff-table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 24px;
            font-size: 13px;
        }

        .diff-table th {
            text-align: left;
            padding: 8px 12px;
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            border-bottom: 2px solid var(--uui-color-border, #e5e7eb);
            background: var(--uui-color-surface-alt, #f9fafb);
        }

        .diff-table td {
            padding: 10px 12px;
            border-bottom: 1px solid var(--uui-color-border, #f3f4f6);
            vertical-align: middle;
        }

        .row-matched    td:first-child { border-left: 3px solid #10b981; }
        .row-typemismatch td:first-child { border-left: 3px solid #f59e0b; }
        .row-unmatched  td:first-child { border-left: 3px solid #ef4444; }

        .row-typemismatch { background: #fffbeb; }
        .row-unmatched    { background: #fff5f5; }

        .alias {
            font-family: monospace;
            font-weight: 600;
        }

        .prop-label {
            font-size: 11px;
            color: var(--uui-color-text-alt, #6b7280);
            margin-left: 6px;
        }

        .status-cell {
            white-space: nowrap;
        }

        .status-icon {
            font-size: 16px;
            margin-right: 4px;
        }

        .row-matched    .status-icon { color: #10b981; }
        .row-typemismatch .status-icon { color: #f59e0b; }
        .row-unmatched  .status-icon { color: #ef4444; }

        .status-text {
            font-size: 11px;
            color: var(--uui-color-text-alt, #6b7280);
        }

        code {
            font-family: monospace;
            font-size: 12px;
            background: var(--uui-color-surface-alt, #f3f4f6);
            padding: 1px 5px;
            border-radius: 3px;
        }

        .na {
            color: var(--uui-color-text-alt, #9ca3af);
            font-style: italic;
        }

        .value-cell .preview-val {
            font-family: monospace;
            font-size: 11px;
            color: #374151;
            word-break: break-all;
        }

        /* Convert section */
        .convert-section {
            border-top: 1px solid var(--uui-color-border, #e5e7eb);
            padding-top: 20px;
        }

        /* Success box */
        .success-box {
            display: flex;
            gap: 12px;
            align-items: flex-start;
            padding: 16px 20px;
            background: #ecfdf5;
            border: 1px solid #6ee7b7;
            border-radius: 6px;
            margin-top: 16px;
        }

        .success-icon {
            font-size: 22px;
            color: #10b981;
            line-height: 1;
        }

        .guid-hint {
            font-family: monospace;
            font-size: 11px;
            color: var(--uui-color-text-alt, #6b7280);
        }

        /* Utility */
        .error {
            color: #ef4444;
            background: #fff5f5;
            border: 1px solid #fca5a5;
            padding: 10px 14px;
            border-radius: 4px;
            margin: 0 0 16px;
        }

        .warn {
            color: #92400e;
            background: #fffbeb;
            border: 1px solid #fcd34d;
            padding: 10px 14px;
            border-radius: 4px;
            margin: 0 0 16px;
        }
    `;
}

declare global {
    interface HTMLElementTagNameMap {
        'vejle-content-converter': ContentConverterElement;
    }
}
