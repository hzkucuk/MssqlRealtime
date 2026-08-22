import {
	HubConnection,
	HubConnectionBuilder,
	HubConnectionState,
	LogLevel
} from '@microsoft/signalr';
import { getAccessToken, getServerUrl } from './client';
import { notify } from '$lib/notify';
import type { AlertNotification, ModuleEvent } from '$lib/types';

/**
 * One hub connection for the whole app, shared by every tool.
 *
 * Modules do not open their own sockets: they subscribe through here, which keeps a phone on
 * mobile data down to a single connection no matter how many tools are installed.
 */

type Handler = (event: ModuleEvent) => void;

class RealtimeClient {
	connection = $state<HubConnection | null>(null);
	state = $state<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');
	lastError = $state<string | null>(null);

	/** Alerts kept in memory for the current session, newest first. */
	alerts = $state<AlertNotification[]>([]);

	/** How many times the initial connection has been retried; shown in the UI. */
	attempts = $state(0);

	/**
	 * The stored session for this panel is spent, so no amount of retrying will connect.
	 * Kept separate from `lastError`: "sign in again" is an instruction to the user, while
	 * a transport error is a report about the network.
	 */
	sessionExpired = $state(false);

	#handlers = new Set<Handler>();
	#wakeBound = false;
	#subscriptions = new Set<string>();
	#retryTimer: ReturnType<typeof setTimeout> | null = null;
	#stopped = false;

	/**
	 * A phone does not tell the page it fell asleep — it just stops running it. When it comes
	 * back the socket is long dead, and waiting out a backoff timer means staring at a red
	 * indicator with live data on the other side. These three events are the moment to try
	 * again, immediately.
	 */
	#bindWakeUp(): void {
		if (this.#wakeBound || typeof document === 'undefined') return;
		this.#wakeBound = true;

		const wake = () => {
			if (this.#stopped) return;
			if (this.state === 'connected' || this.state === 'connecting') return;
			void this.reconnect();
		};

		document.addEventListener('visibilitychange', () => {
			if (document.visibilityState === 'visible') wake();
		});
		window.addEventListener('online', wake);
		window.addEventListener('focus', wake);
	}

	async start(): Promise<void> {
		this.#bindWakeUp();

		if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
			return;
		}

		this.#stopped = false;

		// A spent session cannot be retried into working. getAccessToken() returns null once
		// the refresh token is gone — and the token factory below turned that null into an
		// empty string, so the app kept opening a connection the hub could only answer with
		// 401, every 30 seconds, forever, while the header said nothing but "bağlı değil".
		// Panels are per customer, so this hits exactly one panel: the one signed in longest
		// ago. That is the shape of the bug reported on 2026-08-22.
		if (!(await getAccessToken())) {
			this.state = 'disconnected';
			this.sessionExpired = true;
			this.lastError = 'Bu panelin oturumu sona ermiş.';
			return;
		}

		this.sessionExpired = false;
		this.state = 'connecting';

		const connection = new HubConnectionBuilder()
			.withUrl(`${getServerUrl()}/hubs/tools`, {
				// A WebSocket handshake cannot carry an Authorization header, so the token
				// travels as a query parameter; the host accepts it only for this path.
				accessTokenFactory: async () => (await getAccessToken()) ?? ''
			})
			// 🔴 Measured 2026-08-07: an array here means SignalR gives up after the last entry
			// and never tries again — a phone that slept for a minute came back saying
			// "bağlı değil" forever. The policy below never returns null, so it never gives up;
			// the delay still backs off to 30 s so a phone in a lift is not draining its
			// battery reconnecting every second.
			.withAutomaticReconnect({
				nextRetryDelayInMilliseconds: (context) =>
					context.previousRetryCount === 0
						? 0
						: Math.min(30_000, 2000 * 2 ** (context.previousRetryCount - 1))
			})
			.configureLogging(LogLevel.Warning)
			.build();

		connection.on('moduleEvent', (event: ModuleEvent) => {
			for (const handler of this.#handlers) handler(event);
		});

		connection.on('alert', (alert: AlertNotification) => {
			this.alerts = [alert, ...this.alerts].slice(0, 100);
			void notify(alert);
		});

		connection.onreconnecting(() => {
			this.state = 'reconnecting';
		});

		connection.onreconnected(async () => {
			this.state = 'connected';
			// Group membership lives on the server connection, which is gone after a reconnect.
			await this.#resubscribe();
		});

		connection.onclose((error) => {
			this.state = 'disconnected';
			this.lastError = error?.message ?? null;

			// Reaching onclose means SignalR is done trying. Without this the indicator stayed
			// red until the app was killed and reopened.
			this.#scheduleRetry();
		});

		try {
			await connection.start();
			this.connection = connection;
			this.state = 'connected';
			this.lastError = null;
			this.attempts = 0;
			await this.#resubscribe();
		} catch (error) {
			this.state = 'disconnected';
			this.lastError = error instanceof Error ? error.message : String(error);

			// withAutomaticReconnect only covers a connection that was established and then
			// dropped. A failed FIRST attempt — an expired token at page load, a proxy
			// restarting — would otherwise leave the app saying "bağlı değil" forever.
			this.#scheduleRetry();
		}
	}

	#scheduleRetry(): void {
		if (this.#stopped || this.#retryTimer) return;

		this.attempts++;
		const delays = [2000, 5000, 10000, 30000];
		const delay = delays[Math.min(this.attempts - 1, delays.length - 1)];

		this.#retryTimer = setTimeout(() => {
			this.#retryTimer = null;
			if (!this.#stopped) void this.start();
		}, delay);
	}

	/**
	 * Point the live link at whatever panel is active now.
	 *
	 * Measured 2026-08-09 17:5x: switching customers only rewrote the header. The socket
	 * stayed open against the *previous* customer's hub, the indicator kept saying "canlı",
	 * and every number on screen still belonged to the panel the user had just left — the
	 * new hub was never contacted once. start() alone cannot fix it: it returns early while
	 * a connection is alive, and the hub address is fixed when that connection is built.
	 */
	async switchPanel(): Promise<void> {
		await this.stop();
		// Alerts belong to the panel that raised them.
		this.alerts = [];
		this.attempts = 0;
		await this.start();
	}

	/** Manual retry from the UI; resets the backoff so the user is not made to wait. */
	async reconnect(): Promise<void> {
		if (this.#retryTimer) {
			clearTimeout(this.#retryTimer);
			this.#retryTimer = null;
		}

		this.attempts = 0;
		await this.stop();
		await this.start();
	}

	async stop(): Promise<void> {
		this.#stopped = true;

		if (this.#retryTimer) {
			clearTimeout(this.#retryTimer);
			this.#retryTimer = null;
		}

		await this.connection?.stop();
		this.connection = null;
		this.state = 'disconnected';
		this.#subscriptions.clear();
	}

	onEvent(handler: Handler): () => void {
		this.#handlers.add(handler);
		return () => this.#handlers.delete(handler);
	}

	async subscribeModule(moduleId: string): Promise<void> {
		this.#subscriptions.add(`module:${moduleId}`);
		await this.#invoke('SubscribeModule', moduleId);
	}

	async unsubscribeModule(moduleId: string): Promise<void> {
		this.#subscriptions.delete(`module:${moduleId}`);
		await this.#invoke('UnsubscribeModule', moduleId);
	}

	async subscribeTarget(moduleId: string, targetId: string): Promise<void> {
		this.#subscriptions.add(`target:${moduleId}:${targetId}`);
		await this.#invoke('SubscribeTarget', moduleId, targetId);
	}

	async unsubscribeTarget(moduleId: string, targetId: string): Promise<void> {
		this.#subscriptions.delete(`target:${moduleId}:${targetId}`);
		await this.#invoke('UnsubscribeTarget', moduleId, targetId);
	}

	dismissAlert(key: string): void {
		this.alerts = this.alerts.filter((a) => a.alert.key !== key);
	}

	async #resubscribe(): Promise<void> {
		for (const subscription of this.#subscriptions) {
			const [kind, moduleId, targetId] = subscription.split(':');
			if (kind === 'module') await this.#invoke('SubscribeModule', moduleId);
			else await this.#invoke('SubscribeTarget', moduleId, targetId);
		}
	}

	async #invoke(method: string, ...args: unknown[]): Promise<void> {
		if (this.connection?.state !== HubConnectionState.Connected) return;

		try {
			await this.connection.invoke(method, ...args);
		} catch (error) {
			this.lastError = error instanceof Error ? error.message : String(error);
		}
	}
}

export const realtime = new RealtimeClient();
