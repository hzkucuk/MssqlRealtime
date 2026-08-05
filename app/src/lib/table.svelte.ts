/**
 * Per-table column preferences: which columns are shown and how wide they are.
 *
 * Saved per table in localStorage, because the choice is about this operator's screen and
 * habits — someone watching from a phone hides half the columns; someone on a desktop widens
 * the application name. Neither should have to redo it on every visit.
 */

export type ColumnDef = {
	key: string;
	label: string;
	/** Starting width in pixels. */
	width: number;
	/** Columns that carry the identity of the row cannot be hidden. */
	required?: boolean;
	/** Hidden until the operator asks for it. */
	hiddenByDefault?: boolean;
};

type StoredState = Record<string, { width?: number; hidden?: boolean }>;

const MIN_WIDTH = 48;
const MAX_WIDTH = 640;

export class TableColumns {
	columns = $state<(ColumnDef & { hidden: boolean })[]>([]);

	#storageKey: string;
	#defaults: ColumnDef[];

	constructor(tableId: string, defaults: ColumnDef[]) {
		this.#storageKey = `mr.cols.${tableId}`;
		this.#defaults = defaults;
		this.columns = defaults.map((c) => ({ ...c, hidden: c.hiddenByDefault ?? false }));
		this.#load();
	}

	get visible() {
		return this.columns.filter((c) => !c.hidden);
	}

	get hiddenCount() {
		return this.columns.filter((c) => c.hidden).length;
	}

	isVisible(key: string): boolean {
		return !this.columns.find((c) => c.key === key)?.hidden;
	}

	width(key: string): number {
		return this.columns.find((c) => c.key === key)?.width ?? 120;
	}

	toggle(key: string): void {
		const column = this.columns.find((c) => c.key === key);
		// A required column staying visible is not a preference to be overridden: without it
		// the row cannot be identified or acted on.
		if (!column || column.required) return;

		column.hidden = !column.hidden;
		this.#save();
	}

	setWidth(key: string, width: number): void {
		const column = this.columns.find((c) => c.key === key);
		if (!column) return;

		column.width = Math.round(Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, width)));
		this.#save();
	}

	reset(): void {
		this.columns = this.#defaults.map((c) => ({ ...c, hidden: c.hiddenByDefault ?? false }));

		try {
			localStorage.removeItem(this.#storageKey);
		} catch {
			// Private mode or storage disabled; the in-memory reset still applies.
		}
	}

	#load(): void {
		try {
			const raw = localStorage.getItem(this.#storageKey);
			if (!raw) return;

			const stored = JSON.parse(raw) as StoredState;

			for (const column of this.columns) {
				const saved = stored[column.key];
				if (!saved) continue;

				if (typeof saved.width === 'number') {
					column.width = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, saved.width));
				}

				// A column that became required since the preference was saved stays visible.
				if (typeof saved.hidden === 'boolean' && !column.required) {
					column.hidden = saved.hidden;
				}
			}
		} catch {
			// Corrupt or unreadable preferences are not worth failing a screen over.
		}
	}

	#save(): void {
		try {
			const state: StoredState = {};
			for (const column of this.columns) {
				state[column.key] = { width: column.width, hidden: column.hidden };
			}

			localStorage.setItem(this.#storageKey, JSON.stringify(state));
		} catch {
			// Storage full or blocked: preferences are a convenience, not a requirement.
		}
	}
}
