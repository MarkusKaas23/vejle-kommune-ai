/**
 * Vejle AI — Batch Content Type Converter
 *
 * Two exports in one module:
 *
 *   1. default  → VejleBatchConvertAction
 *      Used by the `entityBulkAction` manifest entry (api field).
 *      Receives the selected node GUIDs and opens the conversion modal.
 *
 *   2. VejleContentConverterBatchModalElement (custom element)
 *      Used by the `modal` manifest entry (elementName field).
 *      Renders the target-type input, merged diff table, progress, and results.
 */

import { UmbEntityBulkActionBase } from '@umbraco-cms/backoffice/entity-bulk-action';
import {
    UMB_MODAL_MANAGER_CONTEXT,
    UmbModalToken,
    UmbModalBaseElement,
} from '@umbraco-cms/backoffice/modal';
import { html, css, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';

// ── API response shapes (mirror C# sealed records) ──────────────────────────

interface PropertyMatchDto {
    alias: string;
    label: string;
    status: 'Matched' | 'TypeMismatch' | 'Unmatched';
    sourceValue?: string;
    sourceEditorAlias: string;
    targetEditorAlias?: string;
}

interface SourceTypeSummaryDto {
    nodeKey: string;
    nodeName: string;
    documentTypeAlias: string;
}

interface BatchCompareResponse {
    sourceNodes: SourceTypeSummaryDto[];
    targetDocumentType: { alias: string; name: string };
    properties: PropertyMatchDto[];
    matchedCount: number;
    typeMismatchCount: number;
    unmatchedCount: number;
}

interface BatchConvertResultDto {
    sourceKey: string;
    newNodeKey: string;
    newNodeName: string;
    propertiesCopied: number;
    propertiesSkipped: number;
}

interface BatchConvertResponse {
    results: BatchConvertResultDto[];
    failures: Array<{ sourceKey: string; error: string }>;
}

// ── Modal token (alias matches the `modal` extension in umbraco-package.json) ─

export const VEJLE_BATCH_CONVERT_MODAL = new UmbModalToken<
    { selection: string[] },
    void
>('VejleAi.ContentConverterBatchModal', {
    modal: { type: 'dialog', size: 'large' },
});

// ── Action class ─────────────────────────────────────────────────────────────

export class VejleBatchConvertAction extends UmbEntityBulkActionBase<never> {
    override async execute(): Promise<void> {
        const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
        const modal = modalManager.open(this, VEJLE_BATCH_CONVERT_MODAL, {
            data: { selection: this.selection as string[] },
        });
        await modal.onSubmit().catch(() => undefined); // cancelled = no-op
    }
}

export default VejleBatchConvertAction;

// ── Modal element ────────────────────────────────────────────────────────────

@customElement('vejle-content-converter-batch-modal')
export class VejleContentConverterBatchModalElement extends UmbModalBaseElement<
    { selection: string[] },
    void
> {
    @state() private _targetAlias = '';
    @state() private _loading = false;
    @state() private _error?: string;
    @state() private _comparison?: BatchCompareResponse;
    @state() private _results?: BatchConvertResponse;

    // ── API calls ────────────────────────────────────────────────────────────

    async #compare(): Promise<void> {
        const sel = this.data?.selection ?? [];
        if (!sel.length || !this._targetAlias.trim()) return;
        this._loading = true;
        this._error = undefined;
        this._comparison = undefined;
        this._results = undefined;
        try {
            const ids = sel.join(',');
            const url =
                `/vejle/api/content-converter/batch-compare` +
                `?sourceIds=${encodeURIComponent(ids)}` +
                `&targetAlias=${encodeURIComponent(this._targetAlias.trim())}` +
                `&culture=da-DK`;
            const resp = await fetch(url);
            if (!resp.ok) {
                const body = await resp.json().catch(() => ({})) as { error?: string };
                this._error = body.error ?? `Compare failed (${resp.status})`;
                return;
            }
            this._comparison = await resp.json() as BatchCompareResponse;
        } catch (err) {
            this._error = String(err);
        } finally {
            this._loading = false;
        }
    }

    async #convert(): Promise<void> {
        const sel = this.data?.selection ?? [];
        if (!sel.length || !this._targetAlias.trim()) return;
        this._loading = true;
        this._error = undefined;
        try {
            const resp = await fetch('/vejle/api/content-converter/batch-convert', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sourceIds: sel,
                    targetAlias: this._targetAlias.trim(),
                    culture: 'da-DK',
                }),
            });
            if (!resp.ok) {
                const body = await resp.json().catch(() => ({})) as { error?: string };
                this._error = body.error ?? `Convert failed (${resp.status})`;
                return;
            }
            this._results = await resp.json() as BatchConvertResponse;
        } catch (err) {
            this._error = String(err);
        } finally {
            this._loading = false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    #statusIcon(s: PropertyMatchDto['status']) {
        return s === 'Matched' ? '✓' : s === 'TypeMismatch' ? '⚠' : '✗';
    }

    #short(alias: string) {
        return alias.replace(/^Umbraco\./, '');
    }

    // ── Template ─────────────────────────────────────────────────────────────

    override render() {
        const sel = this.data?.selection ?? [];
        const c = this._comparison;

        return html`
            <umb-body-layout headline="Convert content type">
                <div id="main">

                    <div class="selection-info">
                        <strong>${sel.length}</strong>
                        node${sel.length !== 1 ? 's' : ''} selected
                    </div>

                    <div class="field-row">
                        <label>Convert to document type alias</label>
                        <input
                            type="text"
                            placeholder="e.g. newsArticle"
                            .value=${this._targetAlias}
                            @input=${(e: Event) => {
                                this._targetAlias = (e.target as HTMLInputElement).value;
                            }}
                        />
                    </div>

                    <div class="actions">
                        <uui-button
                            look="primary"
                            ?disabled=${this._loading || !this._targetAlias.trim()}
                            @click=${this.#compare}
                        >
                            ${this._loading && !c ? 'Comparing…' : 'Compare →'}
                        </uui-button>
                    </div>

                    ${this._error
                        ? html`<p class="error">${this._error}</p>`
                        : nothing}

                    ${c ? this.#renderComparison(c) : nothing}

                    ${this._results ? this.#renderResults(this._results) : nothing}

                </div>
                <div slot="actions">
                    <uui-button @click=${() => this.rejectModal()}>Close</uui-button>
                </div>
            </umb-body-layout>
        `;
    }

    #renderComparison(c: BatchCompareResponse) {
        const canConvert = c.matchedCount > 0 && !this._results;
        const target = c.targetDocumentType.name || c.targetDocumentType.alias;
        return html`
            <div class="comparison">
                <h3>
                    ${c.sourceNodes.length} node${c.sourceNodes.length !== 1 ? 's' : ''}
                    <span class="arrow">→</span> ${target}
                </h3>

                <div class="source-chips">
                    ${c.sourceNodes.map(n => html`
                        <span class="chip">
                            <code>${n.documentTypeAlias}</code> ${n.nodeName}
                        </span>
                    `)}
                </div>

                <div class="pills">
                    <span class="pill match">${c.matchedCount} will be copied</span>
                    ${c.typeMismatchCount > 0
                        ? html`<span class="pill mismatch">${c.typeMismatchCount} type mismatch (skipped)</span>`
                        : nothing}
                    ${c.unmatchedCount > 0
                        ? html`<span class="pill unmatched">${c.unmatchedCount} unmatched (lost)</span>`
                        : nothing}
                </div>

                <table class="diff-table">
                    <thead>
                        <tr>
                            <th>Property</th>
                            <th>Status</th>
                            <th>Source editor</th>
                            <th>Target editor</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${c.properties.map(p => html`
                            <tr class="row-${p.status.toLowerCase()}">
                                <td>
                                    <span class="alias">${p.alias}</span>
                                    ${p.label !== p.alias
                                        ? html`<span class="prop-label">${p.label}</span>`
                                        : nothing}
                                </td>
                                <td class="status-cell">
                                    <span class="status-icon">${this.#statusIcon(p.status)}</span>
                                    ${p.status === 'TypeMismatch'
                                        ? html`<span class="status-text">type diff</span>`
                                        : nothing}
                                    ${p.status === 'Unmatched'
                                        ? html`<span class="status-text">not on target</span>`
                                        : nothing}
                                </td>
                                <td><code>${this.#short(p.sourceEditorAlias)}</code></td>
                                <td>
                                    ${p.targetEditorAlias
                                        ? html`<code>${this.#short(p.targetEditorAlias)}</code>`
                                        : html`<span class="na">—</span>`}
                                </td>
                            </tr>
                        `)}
                    </tbody>
                </table>

                ${canConvert ? html`
                    <div class="actions">
                        <uui-button
                            look="positive"
                            ?disabled=${this._loading}
                            @click=${this.#convert}
                        >
                            ${this._loading
                                ? 'Converting…'
                                : `Convert ${c.sourceNodes.length} node${c.sourceNodes.length !== 1 ? 's' : ''} → ${target}`}
                        </uui-button>
                    </div>
                ` : nothing}
            </div>
        `;
    }

    #renderResults(r: BatchConvertResponse) {
        return html`
            <div class="results">
                <h3>Conversion complete</h3>
                ${r.results.map(res => html`
                    <div class="result-row success">
                        <span class="icon">✓</span>
                        <div>
                            <strong>${res.newNodeName}</strong> created as a draft.
                            ${res.propertiesCopied} ${res.propertiesCopied === 1 ? 'property' : 'properties'} copied,
                            ${res.propertiesSkipped} skipped.
                        </div>
                    </div>
                `)}
                ${r.failures.map(f => html`
                    <div class="result-row failure">
                        <span class="icon">✗</span>
                        <div><code>${f.sourceKey}</code> — ${f.error}</div>
                    </div>
                `)}
            </div>
        `;
    }

    // ── Styles ───────────────────────────────────────────────────────────────

    static override styles = css`
        #main {
            padding: 20px;
            max-width: 860px;
            font-size: 14px;
            font-family: var(--uui-font-family, sans-serif);
        }

        h3 {
            margin: 0 0 12px;
            font-size: 15px;
            font-weight: 600;
        }

        .arrow { color: var(--uui-color-interactive, #3544b1); margin: 0 6px; }

        .selection-info {
            margin-bottom: 16px;
            padding: 10px 14px;
            background: var(--uui-color-surface-alt, #f5f5f5);
            border-radius: 6px;
            font-size: 13px;
        }

        .field-row {
            display: flex;
            flex-direction: column;
            gap: 5px;
            margin-bottom: 16px;
        }

        label { font-weight: 600; font-size: 13px; }

        input[type='text'] {
            padding: 8px 12px;
            border: 1px solid var(--uui-color-border, #d1d5db);
            border-radius: 4px;
            font-size: 14px;
            font-family: monospace;
            max-width: 400px;
        }

        input[type='text']:focus {
            outline: none;
            border-color: var(--uui-color-interactive, #3544b1);
        }

        .actions { margin-bottom: 20px; }

        .source-chips {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin-bottom: 12px;
        }

        .chip {
            font-size: 12px;
            padding: 3px 8px;
            background: var(--uui-color-surface-alt, #f0f0f0);
            border-radius: 4px;
        }

        .chip code {
            color: var(--uui-color-interactive, #3544b1);
            font-size: 11px;
            margin-right: 4px;
        }

        .comparison {
            margin-top: 16px;
            border-top: 2px solid var(--uui-color-border, #e5e7eb);
            padding-top: 20px;
        }

        .pills { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; }
        .pill { padding: 3px 10px; border-radius: 20px; font-size: 12px; font-weight: 600; }
        .pill.match    { background: #d1fae5; color: #065f46; }
        .pill.mismatch { background: #fef3c7; color: #92400e; }
        .pill.unmatched { background: #fee2e2; color: #991b1b; }

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
            padding: 9px 12px;
            border-bottom: 1px solid var(--uui-color-border, #f3f4f6);
            vertical-align: middle;
        }

        .row-matched     td:first-child { border-left: 3px solid #10b981; }
        .row-typemismatch td:first-child { border-left: 3px solid #f59e0b; }
        .row-unmatched   td:first-child { border-left: 3px solid #ef4444; }
        .row-typemismatch { background: #fffbeb; }
        .row-unmatched   { background: #fff5f5; }

        .alias { font-family: monospace; font-weight: 600; }
        .prop-label { font-size: 11px; color: var(--uui-color-text-alt, #6b7280); margin-left: 6px; }
        .status-cell { white-space: nowrap; }
        .status-icon { font-size: 16px; margin-right: 4px; }
        .row-matched     .status-icon { color: #10b981; }
        .row-typemismatch .status-icon { color: #f59e0b; }
        .row-unmatched   .status-icon { color: #ef4444; }
        .status-text { font-size: 11px; color: var(--uui-color-text-alt, #6b7280); }

        code {
            font-family: monospace;
            font-size: 12px;
            background: var(--uui-color-surface-alt, #f3f4f6);
            padding: 1px 5px;
            border-radius: 3px;
        }

        .na { color: var(--uui-color-text-alt, #9ca3af); font-style: italic; }

        .error {
            color: #ef4444;
            background: #fff5f5;
            border: 1px solid #fca5a5;
            padding: 10px 14px;
            border-radius: 4px;
            margin: 0 0 16px;
        }

        .results {
            margin-top: 16px;
            border-top: 2px solid var(--uui-color-border, #e5e7eb);
            padding-top: 16px;
        }

        .result-row {
            display: flex;
            gap: 12px;
            align-items: flex-start;
            padding: 10px 14px;
            border-radius: 6px;
            margin-bottom: 8px;
        }

        .result-row.success { background: #ecfdf5; border: 1px solid #6ee7b7; }
        .result-row.failure { background: #fff5f5; border: 1px solid #fca5a5; }
        .result-row .icon { font-size: 18px; line-height: 1.4; }
        .result-row.success .icon { color: #10b981; }
        .result-row.failure .icon { color: #ef4444; }
    `;
}

declare global {
    interface HTMLElementTagNameMap {
        'vejle-content-converter-batch-modal': VejleContentConverterBatchModalElement;
    }
}
