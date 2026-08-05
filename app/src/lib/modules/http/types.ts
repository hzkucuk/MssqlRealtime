import type { AlertState, Severity } from '$lib/types';

export type HttpCheckStatus = 0 | 1 | 2 | 3;

export type HttpCheckResult = {
	targetId: string;
	targetName: string;
	groupName: string;
	url: string;
	checkedAt: string;
	status: HttpCheckStatus;
	statusCode?: number | null;
	responseTimeMs: number;
	error?: string | null;
	contentLength?: number | null;
	certificateDaysRemaining?: number | null;
	certificateSubject?: string | null;
	uptimePercent?: number | null;
	recentChecks: number;
	activeAlerts: AlertState[];
	severity: Severity;
};

export type HttpTarget = {
	id: string;
	name: string;
	groupName: string;
	url: string;
	method: string;
	expectedStatusCode: number;
	expectedBodyContains?: string | null;
	enabled: boolean;
	checkIntervalSeconds: number;
	timeoutSeconds: number;
	ignoreCertificateErrors: boolean;
	alertOnDown: boolean;
	slowResponseMs?: number | null;
	certificateExpiryWarningDays?: number | null;
	alertConsecutiveBreaches: number;
	alertRenotifyMinutes: number;
	updatedAt: string;
};

export const httpStatusText: Record<number, string> = {
	0: 'Bilinmiyor',
	1: 'Ayakta',
	2: 'Erişilemiyor',
	3: 'Yavaş'
};
