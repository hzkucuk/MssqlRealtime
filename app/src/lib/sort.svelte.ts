/**
 * Click-to-sort for tables.
 *
 * Live data makes sorting more useful than usual and more annoying to get wrong: rows are
 * replaced every few seconds, so the sort has to be re-applied to each incoming snapshot
 * rather than done once to the DOM.
 */

export type SortDirection = 'asc' | 'desc';

export class Sorter<T> {
	key = $state<string>('');
	direction = $state<SortDirection>('desc');

	#accessors: Record<string, (item: T) => unknown>;

	constructor(accessors: Record<string, (item: T) => unknown>, initialKey = '', initialDirection: SortDirection = 'desc') {
		this.#accessors = accessors;
		this.key = initialKey;
		this.direction = initialDirection;
	}

	/** Clicking the active column flips direction; a new column starts descending. */
	toggle(key: string): void {
		if (this.key === key) {
			this.direction = this.direction === 'asc' ? 'desc' : 'asc';
		} else {
			this.key = key;
			// Numbers are almost always interesting from the top (most CPU, longest running).
			this.direction = 'desc';
		}
	}

	/** Arrow for the header; empty when the column is not the active one. */
	indicator(key: string): string {
		if (this.key !== key) return '';
		return this.direction === 'asc' ? '▲' : '▼';
	}

	apply(items: readonly T[]): T[] {
		const accessor = this.#accessors[this.key];
		if (!accessor) return [...items];

		const factor = this.direction === 'asc' ? 1 : -1;

		return [...items].sort((a, b) => factor * compare(accessor(a), accessor(b)));
	}
}

function compare(a: unknown, b: unknown): number {
	// Missing values sink to the bottom in either direction: an empty cell is never the
	// answer to "which is the biggest".
	const aMissing = a === null || a === undefined || a === '';
	const bMissing = b === null || b === undefined || b === '';
	if (aMissing && bMissing) return 0;
	if (aMissing) return 1;
	if (bMissing) return -1;

	if (typeof a === 'number' && typeof b === 'number') return a - b;
	if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);

	// Turkish collation: "İ" and "ı" sort where a Turkish speaker expects them.
	return String(a).localeCompare(String(b), 'tr', { numeric: true, sensitivity: 'base' });
}
