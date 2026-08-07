/**
 * The one place that talks HTTP to the backend.
 *
 * The server address is not baked in: the same build runs on a phone pointing at a customer's
 * host, on a desktop pointing at localhost, and inside the .NET host itself (where it is the
 * same origin).
 *
 * Several servers are supported because that is how the product is actually deployed — one
 * hub per customer, on their own Portainer box. Each saved server keeps its own tokens, so
 * switching customers does not mean signing in again every time.
 */

const SERVERS_KEY = 'mr.servers';
const ACTIVE_KEY = 'mr.activeServer';

export type Tokens = {
	accessToken: string;
	refreshToken: string;
	/** Epoch ms when the access token stops being usable. */
	expiresAt: number;
};

export type SavedServer = {
	/** Normalised URL; also the identity of the entry. */
	url: string;
	/** Customer or site label shown in the switcher. */
	label: string;
	tokens: Tokens | null;
	lastUsedAt: number;
};

export class ApiError extends Error {
	constructor(
		message: string,
		readonly status: number,
		readonly code?: string,
		readonly errors?: string[]
	) {
		super(message);
	}
}

function browserStorage(): Storage | null {
	return typeof localStorage === 'undefined' ? null : localStorage;
}

export function normaliseUrl(url: string): string {
	return url.trim().replace(/\/+$/, '');
}

export function getServers(): SavedServer[] {
	const raw = browserStorage()?.getItem(SERVERS_KEY);
	if (!raw) return [];

	try {
		const parsed = JSON.parse(raw) as SavedServer[];
		return Array.isArray(parsed) ? parsed.sort((a, b) => b.lastUsedAt - a.lastUsedAt) : [];
	} catch {
		return [];
	}
}

function saveServers(servers: SavedServer[]): void {
	browserStorage()?.setItem(SERVERS_KEY, JSON.stringify(servers));
}

export function getServerUrl(): string {
	const active = browserStorage()?.getItem(ACTIVE_KEY);
	if (active) return active;

	// Served by the .NET host itself: talk to the origin it came from.
	if (typeof location !== 'undefined' && location.protocol.startsWith('http')) {
		return location.origin;
	}

	return '';
}

export function getActiveServer(): SavedServer | null {
	const url = getServerUrl();
	return getServers().find((s) => s.url === url) ?? null;
}

/** Makes a saved server the active one. Its stored tokens come along with it. */
export function setActiveServer(url: string): void {
	const normalised = normaliseUrl(url);
	browserStorage()?.setItem(ACTIVE_KEY, normalised);

	const servers = getServers();
	const entry = servers.find((s) => s.url === normalised);
	if (entry) {
		entry.lastUsedAt = Date.now();
		saveServers(servers);
	}
}

export function upsertServer(url: string, label: string, tokens: Tokens | null = null): SavedServer {
	const normalised = normaliseUrl(url);
	const servers = getServers();
	const existing = servers.find((s) => s.url === normalised);

	if (existing) {
		existing.label = label || existing.label;
		if (tokens) existing.tokens = tokens;
		existing.lastUsedAt = Date.now();
		saveServers(servers);
		return existing;
	}

	const entry: SavedServer = { url: normalised, label: label || normalised, tokens, lastUsedAt: Date.now() };
	servers.push(entry);
	saveServers(servers);
	return entry;
}

export function removeServer(url: string): void {
	const normalised = normaliseUrl(url);
	saveServers(getServers().filter((s) => s.url !== normalised));

	if (getServerUrl() === normalised) {
		browserStorage()?.removeItem(ACTIVE_KEY);
	}
}

export function getTokens(): Tokens | null {
	return getActiveServer()?.tokens ?? null;
}

export function setTokens(tokens: Tokens | null): void {
	const url = getServerUrl();
	if (!url) return;

	const servers = getServers();
	const entry = servers.find((s) => s.url === url);
	if (!entry) return;

	entry.tokens = tokens;
	saveServers(servers);
}

type IdentityTokenResponse = {
	accessToken: string;
	refreshToken: string;
	expiresIn: number;
};

function storeIdentityTokens(response: IdentityTokenResponse): Tokens {
	const tokens: Tokens = {
		accessToken: response.accessToken,
		refreshToken: response.refreshToken,
		// Refresh a minute early rather than discovering expiry mid-request.
		expiresAt: Date.now() + (response.expiresIn - 60) * 1000
	};

	setTokens(tokens);
	return tokens;
}

/** The build this bundle was compiled from — injected by Vite from Directory.Build.props. */
export const appVersion = __APP_VERSION__;

/**
 * Which build the hub is running. Needed before sign-in (support calls start with "which
 * version is on that box?"), so it comes from the unauthenticated health endpoint.
 */
export async function fetchServerVersion(serverUrl: string): Promise<string | null> {
	try {
		const response = await fetch(`${normaliseUrl(serverUrl)}/api/health`);
		if (!response.ok) return null;

		return ((await response.json()) as { version?: string }).version ?? null;
	} catch {
		// Unreachable server: the connection indicator already says so; a missing version
		// number must not put an error on screen by itself.
		return null;
	}
}

/**
 * Orders two semver strings: negative when `a` is older, 0 when equal, positive when newer.
 * Only the numeric parts matter here — this product has never shipped a pre-release tag, and
 * guessing at one would be worse than ignoring it.
 */
export function compareVersions(a: string, b: string): number {
	const parts = (v: string) => v.replace(/^v/, '').split('.').map((n) => Number.parseInt(n, 10) || 0);
	const [left, right] = [parts(a), parts(b)];

	for (let i = 0; i < Math.max(left.length, right.length); i++) {
		const diff = (left[i] ?? 0) - (right[i] ?? 0);
		if (diff !== 0) return diff;
	}

	return 0;
}

/**
 * Where a phone downloads a newer build.
 *
 * 🔴 Ölçüldü 2026-08-07: burada panelin sürümüne ait etiket sayfası açılıyordu, ama o
 * sürümde APK yayınlanmamışsa (yalnız sunucu tarafı değiştiyse) kullanıcı boş bir sayfaya
 * düşüyordu — "yeni sürüm var" diyen bir şerit, indirilecek bir şey göstermeden. Artık her
 * zaman **en son yayın** açılıyor; orada APK her zaman bulunur.
 */
export function releasePageUrl(_version: string): string {
	return 'https://github.com/hzkucuk/MssqlRealtime/releases/latest';
}

export type CaptchaChallenge = { token: string; svg: string };

/** Whether this address already has to solve a captcha before its next attempt. */
export async function isCaptchaRequired(serverUrl: string): Promise<boolean> {
	try {
		const response = await fetch(`${normaliseUrl(serverUrl)}/api/auth/captcha/required`);
		if (!response.ok) return false;

		return ((await response.json()) as { required: boolean }).required;
	} catch {
		// Unreachable server: let the sign-in attempt produce the real error message.
		return false;
	}
}

export async function fetchCaptcha(serverUrl: string): Promise<CaptchaChallenge> {
	const response = await fetch(`${normaliseUrl(serverUrl)}/api/auth/captcha`);
	if (!response.ok) {
		throw new ApiError('Güvenlik kodu alınamadı.', response.status);
	}

	return (await response.json()) as CaptchaChallenge;
}

export async function login(
	serverUrl: string,
	email: string,
	password: string,
	label = '',
	captcha?: { token: string; answer: string }
): Promise<void> {
	const url = normaliseUrl(serverUrl);

	// Register the entry first so setTokens has somewhere to write.
	upsertServer(url, label);
	setActiveServer(url);

	const headers: Record<string, string> = { 'Content-Type': 'application/json' };

	if (captcha) {
		headers['X-Captcha-Token'] = captcha.token;
		headers['X-Captcha-Answer'] = captcha.answer;
	}

	const response = await fetch(`${url}/api/auth/login`, {
		method: 'POST',
		headers,
		body: JSON.stringify({ email, password })
	});

	if (!response.ok) {
		// The server flags this so the screen can show a captcha instead of a dead end.
		if (response.headers.get('X-Captcha-Required') === 'true') {
			throw new ApiError('Güvenlik kodu hatalı veya süresi dolmuş.', response.status, 'captcha_required');
		}

		if (response.status === 429) {
			throw new ApiError(
				'Çok fazla deneme yapıldı. Bir dakika bekleyip tekrar deneyin.',
				429,
				'rate_limited'
			);
		}

		throw new ApiError(
			response.status === 401
				? 'Kullanıcı adı veya parola hatalı.'
				: `Giriş başarısız (HTTP ${response.status}).`,
			response.status
		);
	}

	storeIdentityTokens((await response.json()) as IdentityTokenResponse);
}

/** Signs out of the active server only; other saved servers keep their sessions. */
export function logout(): void {
	setTokens(null);
}

/** Returns a usable access token, refreshing it first if it is about to expire. */
export async function getAccessToken(): Promise<string | null> {
	const tokens = getTokens();
	if (!tokens) return null;

	if (Date.now() < tokens.expiresAt) {
		return tokens.accessToken;
	}

	const response = await fetch(`${getServerUrl()}/api/auth/refresh`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ refreshToken: tokens.refreshToken })
	});

	if (!response.ok) {
		// The refresh token is spent or revoked: this session is over.
		setTokens(null);
		return null;
	}

	return storeIdentityTokens((await response.json()) as IdentityTokenResponse).accessToken;
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
	const token = await getAccessToken();
	if (!token) {
		throw new ApiError('Oturum sona erdi. Yeniden giriş yapın.', 401, 'unauthenticated');
	}

	const headers = new Headers(init.headers);
	headers.set('Authorization', `Bearer ${token}`);
	if (init.body && !headers.has('Content-Type')) {
		headers.set('Content-Type', 'application/json');
	}

	const response = await fetch(`${getServerUrl()}${path}`, { ...init, headers });

	if (response.status === 204) {
		return undefined as T;
	}

	const payload = await response.json().catch(() => null);

	if (!response.ok) {
		const body = payload as { error?: string; errors?: string[]; code?: string } | null;
		throw new ApiError(
			body?.error ?? body?.errors?.join(' ') ?? `İstek başarısız (HTTP ${response.status}).`,
			response.status,
			body?.code,
			body?.errors
		);
	}

	return payload as T;
}
