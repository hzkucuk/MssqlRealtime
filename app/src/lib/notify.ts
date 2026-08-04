import type { AlertNotification } from './types';

/**
 * Raises a real notification for an alert.
 *
 * Inside Tauri this is the OS notification centre (iOS, Android, Windows, macOS, Linux);
 * in a browser it falls back to the Web Notifications API. Both are best-effort — the alert
 * is always in the in-app list regardless, because a suppressed notification must never mean
 * a lost alert.
 */

let tauriNotification: typeof import('@tauri-apps/plugin-notification') | null = null;
let tauriChecked = false;
let permissionGranted: boolean | null = null;

function isTauri(): boolean {
	return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

async function loadTauri() {
	if (!tauriChecked) {
		tauriChecked = true;
		if (isTauri()) {
			tauriNotification = await import('@tauri-apps/plugin-notification');
		}
	}

	return tauriNotification;
}

/** Asks for permission up front, so the first real alert is not the thing that prompts. */
export async function ensureNotificationPermission(): Promise<boolean> {
	const tauri = await loadTauri();

	if (tauri) {
		permissionGranted = await tauri.isPermissionGranted();
		if (!permissionGranted) {
			permissionGranted = (await tauri.requestPermission()) === 'granted';
		}

		return permissionGranted;
	}

	if (typeof Notification === 'undefined') return false;

	if (Notification.permission === 'default') {
		permissionGranted = (await Notification.requestPermission()) === 'granted';
	} else {
		permissionGranted = Notification.permission === 'granted';
	}

	return permissionGranted;
}

export async function notify(alert: AlertNotification): Promise<void> {
	const granted = permissionGranted ?? (await ensureNotificationPermission());
	if (!granted) return;

	const title = alert.title;
	const body = alert.isCleared
		? `${alert.alert.ruleTitle} normale döndü.`
		: `${alert.alert.target.groupName ? alert.alert.target.groupName + ' · ' : ''}${alert.body}`;

	const tauri = await loadTauri();
	if (tauri) {
		tauri.sendNotification({ title, body });
		return;
	}

	if (typeof Notification !== 'undefined') {
		// Tagging by rule collapses repeats of the same alert instead of stacking them.
		new Notification(title, { body, tag: alert.alert.key });
	}
}
