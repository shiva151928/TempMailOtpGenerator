/** Mirrors the DTOs in backend/src/TempMailOtp.Api/Contracts/TempMailContracts.cs (camelCase JSON). */

export interface ProviderInfo {
  provider: string;
  displayName: string;
  isDemo: boolean;
  attributionText: string | null;
  attributionUrl: string | null;
  clientPollIntervalSeconds: number;
  inboxLifetimeMinutes: number;
}

export interface GeneratedInbox {
  inboxId: string;
  accessToken: string;
  address: string;
  createdAt: string;
  expiresAt: string;
  provider: string;
  isDemo: boolean;
  pollIntervalSeconds: number;
}

/** What the browser keeps (in localStorage) to reopen the inbox after a reload. */
export interface InboxSession {
  inboxId: string;
  accessToken: string;
  address: string;
  createdAt: string;
  expiresAt: string;
  provider: string;
  isDemo: boolean;
  pollIntervalSeconds: number;
}

export interface InboxStatus {
  inboxId: string;
  address: string;
  createdAt: string;
  expiresAt: string;
  isExpired: boolean;
  secondsRemaining: number;
  messageCount: number;
  provider: string;
  isDemo: boolean;
  lastSyncedAt: string | null;
  lastSyncError: string | null;
  pollIntervalSeconds: number;
  serverTime: string;
}

export interface MessageSummary {
  id: string;
  fromAddress: string;
  fromName: string | null;
  subject: string;
  preview: string;
  receivedAt: string;
  isRead: boolean;
  hasAttachments: boolean;
  otpCode: string | null;
}

export interface Otp {
  code: string;
  confidence: number;
  messageId: string;
  senderAddress: string;
  senderName: string | null;
  subject: string;
  receivedAt: string;
  detectedAt: string;
  source: string;
  matchedPhrase: string | null;
}

export interface MessageDetail {
  id: string;
  fromAddress: string;
  fromName: string | null;
  subject: string;
  receivedAt: string;
  hasAttachments: boolean;
  textBody: string | null;
  sanitizedHtml: string | null;
  otp: Otp | null;
}

export interface InboxMessages {
  inboxId: string;
  messages: MessageSummary[];
  latestOtp: Otp | null;
  newMessageCount: number;
  lastSyncedAt: string | null;
  syncError: string | null;
  expiresAt: string;
  serverTime: string;
}

export interface OtpHistory {
  latest: Otp | null;
  history: Otp[];
}

export interface DemoSimulation {
  message: string;
  expectedCode: string;
}
