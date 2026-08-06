/** Display helpers. Turkish output, tabular-friendly, and honest about missing values. */

export function num(value: number | null | undefined, digits = 0): string {
	if (value === null || value === undefined || Number.isNaN(value)) return '—';
	return value.toLocaleString('tr-TR', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

export function pct(value: number | null | undefined, digits = 0): string {
	return value === null || value === undefined ? '—' : `%${num(value, digits)}`;
}

export function mb(value: number | null | undefined): string {
	if (value === null || value === undefined) return '—';
	return value >= 1024 ? `${num(value / 1024, 1)} GB` : `${num(value)} MB`;
}

export function duration(seconds: number | null | undefined): string {
	if (seconds === null || seconds === undefined) return '—';
	if (seconds < 60) return `${Math.round(seconds)} sn`;
	if (seconds < 3600) return `${Math.floor(seconds / 60)} dk ${Math.round(seconds % 60)} sn`;

	const hours = Math.floor(seconds / 3600);
	if (hours < 24) return `${hours} sa ${Math.floor((seconds % 3600) / 60)} dk`;
	return `${Math.floor(hours / 24)} gün ${hours % 24} sa`;
}

export function ago(iso: string | null | undefined): string {
	if (!iso) return '—';

	const seconds = (Date.now() - new Date(iso).getTime()) / 1000;
	if (seconds < 2) return 'şimdi';
	if (seconds < 60) return `${Math.round(seconds)} sn önce`;
	if (seconds < 3600) return `${Math.floor(seconds / 60)} dk önce`;
	if (seconds < 86400) return `${Math.floor(seconds / 3600)} sa önce`;
	return `${Math.floor(seconds / 86400)} gün önce`;
}

export function clock(iso: string | null | undefined): string {
	if (!iso) return '—';
	return new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

export function dateTime(iso: string | null | undefined): string {
	if (!iso) return '—';
	return new Date(iso).toLocaleString('tr-TR', {
		day: '2-digit',
		month: '2-digit',
		year: 'numeric',
		hour: '2-digit',
		minute: '2-digit'
	});
}

export const statusText: Record<number, string> = {
	0: 'Bilinmiyor',
	1: 'Çevrimiçi',
	2: 'Erişilemiyor',
	3: 'Hata'
};

export const severityText: Record<number, string> = {
	0: 'Normal',
	1: 'Uyarı',
	2: 'Kritik'
};

/**
 * "15.0.4123.1" → "SQL Server 2019". The major number is the only reliable marker: the
 * build changes with every cumulative update and nobody recognises it, while "2019" is what
 * people call the thing in conversation.
 */
export function sqlServerName(productVersion: string | null | undefined): string | null {
	const major = Number.parseInt((productVersion ?? '').split('.')[0] ?? '', 10);
	if (!major) return null;

	const names: Record<number, string> = {
		17: 'SQL Server 2025',
		16: 'SQL Server 2022',
		15: 'SQL Server 2019',
		14: 'SQL Server 2017',
		13: 'SQL Server 2016',
		12: 'SQL Server 2014',
		11: 'SQL Server 2012',
		10: 'SQL Server 2008'
	};

	return names[major] ?? `SQL Server (sürüm ${major})`;
}

/**
 * The Edition string is a mouthful — "Express Edition (64-bit)", "Enterprise Edition: Core-based
 * Licensing (64-bit)". Only the first word decides what the server can do, and that is what
 * belongs next to a server's name.
 */
export function sqlEdition(edition: string | null | undefined): string | null {
	if (!edition) return null;

	const known = ['Express', 'Standard', 'Enterprise', 'Developer', 'Web', 'Business Intelligence', 'Azure'];
	return known.find((e) => edition.startsWith(e)) ?? edition.replace(/ Edition.*$/, '');
}
