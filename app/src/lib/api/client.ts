/**
 * The one place that talks HTTP to the backend.
 *
 * The server address is not baked in: the same build runs on a phone pointing at a customer's
 * host, on a desktop pointing at localhost, and inside the .NET host itself (where it is the
 * same origin). It is asked for once, on the sign-in screen, and kept in localStorage.
 */

const SERVER_KEY = 'mr.serverUrl';
const TOKEN_KEY = 'mr.tokens';

export type Tokens = {
	accessToken: string;
	refreshToken: string;
	/** Epoch ms when the access token stops being usable. */
	expiresAt: number;
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

export function getServerUrl(): string {
	const stored = browserStorage()?.getItem(SERVER_KEY);
	if (stored) return stored;

	// Served by the .NET host itself: talk to the origin it came from.
	if (typeof location !== 'undefined' && location.protocol.startsWith('http')) {
		return location.origin;
	}

	return '';
}

export function setServerUrl(url: string): void {
	browserStorage()?.setItem(SERVER_KEY, url.replace(/\/+$/, ''));
}

export function getTokens(): Tokens | null {
	const raw = browserStorage()?.getItem(TOKEN_KEY);
	if (!raw) return null;

	try {
		return JSON.parse(raw) as Tokens;
	} catch {
		return null;
	}
}

export function setTokens(tokens: Tokens | null): void {
	const storage = browserStorage();
	if (!storage) return;

	if (tokens) storage.setItem(TOKEN_KEY, JSON.stringify(tokens));
	else storage.removeItem(TOKEN_KEY);
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

export async function login(serverUrl: string, email: string, password: string): Promise<void> {
	setServerUrl(serverUrl);

	const response = await fetch(`${getServerUrl()}/api/auth/login`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ email, password })
	});

	if (!response.ok) {
		throw new ApiError(
			response.status === 401
				? 'Kullanıcı adı veya parola hatalı.'
				: `Giriş başarısız (HTTP ${response.status}).`,
			response.status
		);
	}

	storeIdentityTokens((await response.json()) as IdentityTokenResponse);
}

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
