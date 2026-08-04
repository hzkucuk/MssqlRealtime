/** Shapes the backend sends. Kept flat and optional-tolerant: an older app must survive a
 *  newer server adding fields, which is the normal state of affairs once phones are in the wild. */

export type ToolDescriptor = {
	id: string;
	title: string;
	icon: string;
	order: number;
	version: string;
	description?: string | null;
	capabilities: string[];
};

export type ModuleEvent = {
	moduleId: string;
	targetId: string | null;
	event: string;
	payload: unknown;
	sentAt: string;
};

export type Severity = 0 | 1 | 2;

export type AlertState = {
	target: { moduleId: string; targetId: string; targetName: string; groupName?: string | null };
	ruleId: string;
	ruleTitle: string;
	severity: Severity;
	message: string;
	value?: number | null;
	threshold?: number | null;
	unit?: string | null;
	sinceUtc: string;
	lastNotifiedUtc?: string | null;
	key: string;
};

export type AlertNotification = {
	alert: AlertState;
	isCleared: boolean;
	raisedAtUtc: string;
	title: string;
	body: string;
};

// --- MSSQL module ---------------------------------------------------------------------------

export type ServerStatus = 0 | 1 | 2 | 3;

export type ServerSummary = {
	totalSessions: number;
	userSessions: number;
	activeRequests: number;
	blockedSessions: number;
	blockingHeads: number;
	longestRunningSeconds: number;
	openTransactions: number;
	cpuPercent?: number | null;
	memoryUsedPercent?: number | null;
	topWaitType?: string | null;
	distinctApplications: number;
	distinctHosts: number;
	severity: Severity;
};

export type MachineResources = {
	cpuPercent?: number | null;
	sqlCpuPercent?: number | null;
	cpuSampleAgeSeconds?: number | null;
	totalPhysicalMemoryMb?: number | null;
	availablePhysicalMemoryMb?: number | null;
	memoryUsedPercent?: number | null;
	sqlProcessMemoryMb?: number | null;
	sqlTargetMemoryMb?: number | null;
	systemMemoryState?: string | null;
	pageLifeExpectancySeconds?: number | null;
	schedulerCount: number;
	runnableTasks: number;
};

export type SqlInstanceInfo = {
	serverName?: string | null;
	productVersion?: string | null;
	productLevel?: string | null;
	edition?: string | null;
	startedAt?: string | null;
	uptimeMinutes?: number | null;
	cpuCount?: number | null;
	hostPlatform?: string | null;
};

export type SessionInfo = {
	sessionId: number;
	loginName?: string | null;
	hostName?: string | null;
	programName?: string | null;
	clientAddress?: string | null;
	status?: string | null;
	databaseName?: string | null;
	loginTime?: string | null;
	lastRequestEnd?: string | null;
	cpuTimeMs: number;
	reads: number;
	writes: number;
	logicalReads: number;
	memoryUsageKb: number;
	openTransactionCount: number;
	isBlocked: boolean;
	isBlocker: boolean;
	idleSeconds: number;
};

export type RequestInfo = {
	sessionId: number;
	status?: string | null;
	command?: string | null;
	databaseName?: string | null;
	loginName?: string | null;
	hostName?: string | null;
	programName?: string | null;
	elapsedSeconds: number;
	cpuTimeMs: number;
	logicalReads: number;
	blockingSessionId?: number | null;
	waitType?: string | null;
	waitResource?: string | null;
	waitTimeMs: number;
	percentComplete?: number | null;
	sqlText?: string | null;
};

export type BlockingEdge = {
	blockedSessionId: number;
	blockingSessionId: number;
	waitTimeMs: number;
	waitType?: string | null;
	waitResource?: string | null;
	blockedProgram?: string | null;
	blockingProgram?: string | null;
	blockedLogin?: string | null;
	blockingLogin?: string | null;
	blockedSql?: string | null;
	blockingSql?: string | null;
};

export type WaitStat = {
	waitType: string;
	waitTimeMs: number;
	waitingTasks: number;
	percentage: number;
};

export type DatabaseInfo = {
	name: string;
	state?: string | null;
	recoveryModel?: string | null;
	dataSizeMb?: number | null;
	logSizeMb?: number | null;
	lastFullBackup?: string | null;
	isReadCommittedSnapshotOn: boolean;
};

export type SqlServiceInfo = {
	serviceName: string;
	serviceAccount?: string | null;
	statusDescription?: string | null;
	startupType?: string | null;
	lastStartupTime?: string | null;
};

export type ServerSnapshot = {
	serverId: string;
	serverName: string;
	customerName: string;
	capturedAt: string;
	status: ServerStatus;
	collectionMs: number;
	errorMessage?: string | null;
	summary: ServerSummary;
	resources?: MachineResources | null;
	instance?: SqlInstanceInfo | null;
	sessions: SessionInfo[];
	requests: RequestInfo[];
	blocking: BlockingEdge[];
	topWaits: WaitStat[];
	databases: DatabaseInfo[];
	services: SqlServiceInfo[];
	activeAlerts: AlertState[];
};

export type AlertThresholds = {
	cpuPercent?: number | null;
	memoryPercent?: number | null;
	sqlProcessMemoryMb?: number | null;
	blockedSessions?: number | null;
	longRunningQuerySeconds?: number | null;
	sessionCount?: number | null;
	consecutiveBreaches: number;
	renotifyMinutes: number;
	alertOnOffline: boolean;
};

export type ServerProfile = {
	id: string;
	name: string;
	customerName: string;
	host: string;
	port: number;
	initialCatalog: string;
	authMode: 0 | 1;
	username?: string | null;
	hasPassword: boolean;
	encryptConnection: boolean;
	trustServerCertificate: boolean;
	connectTimeoutSeconds: number;
	commandTimeoutSeconds: number;
	enabled: boolean;
	pollIntervalSeconds: number;
	thresholds: AlertThresholds;
	updatedAt: string;
};
