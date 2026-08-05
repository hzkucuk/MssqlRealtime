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

	#handlers = new Set<Handler>();
	#subscriptions = new Set<string>();
	#retryTimer: ReturnType<typeof setTimeout> | null = null;
	#stopped = false;

	async start(): Promise<void> {
		if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
			return;
		}

		this.#stopped = false;
		this.state = 'connecting';

		const connection = new HubConnectionBuilder()
			.withUrl(`${getServerUrl()}/hubs/tools`, {
				// A WebSocket handshake cannot carry an Authorization header, so the token
				// travels as a query parameter; the host accepts it only for this path.
				accessTokenFactory: async () => (await getAccessToken()) ?? ''
			})
			// Backing off rather than hammering: a phone that loses signal in a lift should
			// not spend its battery reconnecting every second.
			.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
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
