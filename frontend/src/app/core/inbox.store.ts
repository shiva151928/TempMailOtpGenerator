import { Injectable, computed, inject, signal } from '@angular/core';
import { EMPTY, Subscription, catchError, exhaustMap, filter, switchMap, timer } from 'rxjs';
import { environment } from '../../environments/environment';
import { toApiError } from './api-error';
import { NotificationService } from './notification.service';
import { TempMailApiService } from './temp-mail-api.service';
import { InboxMessages, InboxSession, MessageDetail, MessageSummary, Otp, ProviderInfo } from './temp-mail.models';

const STORAGE_KEY = 'tempmail-otp.session.v1';

/**
 * Application state for the current temporary inbox: session, polling, messages and OTPs.
 * Components read the signals and call the action methods; all HTTP goes through TempMailApiService.
 */
@Injectable({ providedIn: 'root' })
export class InboxStore {
  private readonly api = inject(TempMailApiService);
  private readonly notifications = inject(NotificationService);

  readonly provider = signal<ProviderInfo | null>(null);
  readonly providerError = signal<string | null>(null);

  readonly session = signal<InboxSession | null>(null);
  readonly messages = signal<MessageSummary[]>([]);
  readonly latestOtp = signal<Otp | null>(null);
  readonly otpHistory = signal<Otp[]>([]);
  readonly lastSyncedAt = signal<string | null>(null);
  readonly syncError = signal<string | null>(null);
  readonly expired = signal(false);

  readonly selectedMessageId = signal<string | null>(null);
  readonly selectedMessage = signal<MessageDetail | null>(null);
  readonly messageLoading = signal(false);
  readonly messageError = signal<string | null>(null);

  readonly generating = signal(false);
  readonly refreshing = signal(false);
  readonly deleting = signal(false);
  readonly simulating = signal(false);
  /** True until the first message list arrives for a new or restored inbox. */
  readonly initialLoading = signal(false);

  /** Ticks every second; drives the countdown and relative times. */
  readonly now = signal(Date.now());
  /** Changes whenever a new OTP arrives, so the OTP box can highlight it. */
  readonly otpArrivedAt = signal(0);

  readonly hasInbox = computed(() => this.session() !== null);
  readonly isDemo = computed(() => this.session()?.isDemo ?? this.provider()?.isDemo ?? false);
  readonly pollIntervalSeconds = computed(
    () =>
      this.session()?.pollIntervalSeconds ??
      this.provider()?.clientPollIntervalSeconds ??
      environment.defaultPollIntervalSeconds,
  );
  readonly secondsRemaining = computed(() => {
    const session = this.session();
    if (!session) {
      return 0;
    }
    const expiresAt = Date.parse(session.expiresAt);
    return Math.max(0, Math.ceil((expiresAt - (this.now() + this.clockOffsetMs)) / 1000));
  });
  readonly lifetimeSeconds = computed(() => {
    const session = this.session();
    if (!session) {
      return 0;
    }
    return Math.max(1, Math.round((Date.parse(session.expiresAt) - Date.parse(session.createdAt)) / 1000));
  });
  readonly unreadCount = computed(() => this.messages().filter((message) => !message.isRead).length);

  /** Server time minus browser time, so the countdown follows the server's clock. */
  private clockOffsetMs = 0;
  private pollSubscription: Subscription | null = null;
  private messageSubscription: Subscription | null = null;
  private clockHandle: ReturnType<typeof setInterval> | null = null;
  private hasLoadedMessages = false;

  init(): void {
    this.startClock();
    this.loadProvider();

    const saved = this.readSavedSession();
    if (saved) {
      this.restore(saved);
    }
  }

  dispose(): void {
    this.stopPolling();
    this.messageSubscription?.unsubscribe();
    if (this.clockHandle !== null) {
      clearInterval(this.clockHandle);
      this.clockHandle = null;
    }
  }

  generate(): void {
    if (this.generating()) {
      return;
    }

    const previous = this.session();
    const previousWasLive = previous !== null && !this.expired();
    this.generating.set(true);

    this.api.generate().subscribe({
      next: (inbox) => {
        this.generating.set(false);
        this.stopPolling();
        this.resetInboxState();

        const session: InboxSession = {
          inboxId: inbox.inboxId,
          accessToken: inbox.accessToken,
          address: inbox.address,
          createdAt: inbox.createdAt,
          expiresAt: inbox.expiresAt,
          provider: inbox.provider,
          isDemo: inbox.isDemo,
          pollIntervalSeconds: inbox.pollIntervalSeconds,
        };
        this.session.set(session);
        this.saveSession(session);
        this.notifications.success('New address ready. Copy it into the sign-up form.');
        this.initialLoading.set(true);
        this.startPolling();

        // Free the previous mailbox at the provider; failures here don't affect the new inbox.
        if (previous && previousWasLive) {
          this.api.deleteInbox(previous).subscribe({ error: () => undefined });
        }
      },
      error: (error: unknown) => {
        this.generating.set(false);
        this.notifications.error(`Couldn't generate an address. ${toApiError(error).message}`);
      },
    });
  }

  refresh(): void {
    const session = this.session();
    if (!session || this.refreshing() || this.expired()) {
      return;
    }

    this.refreshing.set(true);
    this.api.refresh(session).subscribe({
      next: (result) => {
        this.refreshing.set(false);
        this.applyMessages(result, true);
        const count = result.newMessageCount;
        if (count > 0) {
          this.notifications.success(`${count} new message${count === 1 ? '' : 's'}.`);
        } else {
          this.notifications.info('Inbox is up to date.');
        }
      },
      error: (error: unknown) => {
        this.refreshing.set(false);
        this.handleInboxError(error, true);
      },
    });
  }

  deleteInbox(): void {
    const session = this.session();
    if (!session || this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.api.deleteInbox(session).subscribe({
      next: () => {
        this.deleting.set(false);
        this.clearSession();
        this.notifications.success('Inbox deleted.');
      },
      error: (error: unknown) => {
        this.deleting.set(false);
        const apiError = toApiError(error);
        if (apiError.status === 404) {
          this.clearSession();
          this.notifications.info('That inbox was already deleted.');
        } else {
          this.notifications.error(`Couldn't delete the inbox. ${apiError.message}`);
        }
      },
    });
  }

  simulateDemoOtp(): void {
    const session = this.session();
    if (!session || !session.isDemo || this.simulating() || this.expired()) {
      return;
    }

    this.simulating.set(true);
    this.api
      .simulateDemoOtp(session)
      .pipe(switchMap(() => this.api.refresh(session)))
      .subscribe({
        next: (result) => {
          this.simulating.set(false);
          this.applyMessages(result, false);
        },
        error: (error: unknown) => {
          this.simulating.set(false);
          this.handleInboxError(error, true);
        },
      });
  }

  openMessage(messageId: string): void {
    const session = this.session();
    if (!session) {
      return;
    }

    this.selectedMessageId.set(messageId);
    this.selectedMessage.set(null);
    this.messageError.set(null);
    this.messageLoading.set(true);

    this.messageSubscription?.unsubscribe();
    this.messageSubscription = this.api.getMessage(session, messageId).subscribe({
      next: (message) => {
        this.messageLoading.set(false);
        this.selectedMessage.set(message);
        this.messages.update((list) => list.map((item) => (item.id === messageId ? { ...item, isRead: true } : item)));
      },
      error: (error: unknown) => {
        this.messageLoading.set(false);
        const apiError = toApiError(error);
        this.messageError.set(apiError.message);
        if (apiError.code === 'inbox_expired' || apiError.status === 410) {
          this.markExpired(true);
        }
      },
    });
  }

  closeMessage(): void {
    this.messageSubscription?.unsubscribe();
    this.selectedMessageId.set(null);
    this.selectedMessage.set(null);
    this.messageError.set(null);
    this.messageLoading.set(false);
  }

  private loadProvider(): void {
    this.api.getProvider().subscribe({
      next: (provider) => {
        this.provider.set(provider);
        this.providerError.set(null);
      },
      error: (error: unknown) => this.providerError.set(toApiError(error).message),
    });
  }

  private restore(saved: InboxSession): void {
    this.session.set(saved);
    this.initialLoading.set(true);

    this.api.getStatus(saved).subscribe({
      next: (status) => {
        this.applyServerTime(status.serverTime);
        this.session.update((session) => (session ? { ...session, expiresAt: status.expiresAt } : session));
        if (status.isExpired) {
          this.initialLoading.set(false);
          this.markExpired(false);
          return;
        }
        this.startPolling();
      },
      error: (error: unknown) => {
        this.initialLoading.set(false);
        const apiError = toApiError(error);
        if (apiError.status === 404 || apiError.status === 401) {
          this.clearSession();
        } else {
          this.syncError.set(apiError.message);
          this.startPolling();
        }
      },
    });
  }

  private startPolling(): void {
    this.stopPolling();
    const session = this.session();
    if (!session) {
      return;
    }

    const intervalMs = Math.max(3, this.pollIntervalSeconds()) * 1000;
    this.pollSubscription = timer(0, intervalMs)
      .pipe(
        // In a background tab, poll four times less often.
        filter((tick) => !document.hidden || tick % 4 === 0),
        exhaustMap(() =>
          this.api.getMessages(session).pipe(
            catchError((error: unknown) => {
              this.handleInboxError(error, false);
              return EMPTY;
            }),
          ),
        ),
      )
      .subscribe((result) => this.applyMessages(result, false));
  }

  private stopPolling(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = null;
  }

  private applyMessages(result: InboxMessages, manual: boolean): void {
    const firstLoad = !this.hasLoadedMessages;
    const knownIds = new Set(this.messages().map((message) => message.id));

    this.initialLoading.set(false);
    this.applyServerTime(result.serverTime);
    this.messages.set(result.messages);
    this.lastSyncedAt.set(result.lastSyncedAt);
    this.session.update((session) =>
      session && session.expiresAt !== result.expiresAt ? { ...session, expiresAt: result.expiresAt } : session,
    );

    if (result.syncError && result.syncError !== this.syncError() && !manual) {
      this.notifications.warning(`Couldn't check for new mail: ${result.syncError}`);
    }
    this.syncError.set(result.syncError);

    const latest = result.latestOtp;
    const current = this.latestOtp();
    const otpChanged = latest !== null && (current === null || current.messageId !== latest.messageId);
    if (otpChanged) {
      this.latestOtp.set(latest);
      this.loadOtpHistory();
      if (!firstLoad) {
        this.otpArrivedAt.set(Date.now());
        this.notifications.success(`New code from ${latest.senderName || latest.senderAddress}.`);
      }
    } else if (latest === null && current !== null) {
      this.latestOtp.set(null);
      this.otpHistory.set([]);
    }

    if (!firstLoad && !manual && !otpChanged) {
      const arrived = result.messages.filter((message) => !knownIds.has(message.id));
      if (arrived.length > 0) {
        const sender = arrived[0].fromName || arrived[0].fromAddress;
        this.notifications.info(arrived.length === 1 ? `New message from ${sender}.` : `${arrived.length} new messages.`);
      }
    }

    this.hasLoadedMessages = true;
  }

  private loadOtpHistory(): void {
    const session = this.session();
    if (!session) {
      return;
    }

    this.api.getOtp(session).subscribe({
      next: (result) => this.otpHistory.set(result.history),
      error: () => undefined, // The latest code is already shown; history is secondary.
    });
  }

  private handleInboxError(error: unknown, manual: boolean): void {
    this.initialLoading.set(false);
    const apiError = toApiError(error);

    if (apiError.code === 'inbox_expired' || apiError.status === 410) {
      this.markExpired(true);
      return;
    }

    if (apiError.code === 'inbox_not_found' || apiError.code === 'inbox_token_missing') {
      this.clearSession();
      this.notifications.error('This inbox no longer exists. Generate a new address.');
      return;
    }

    if (manual) {
      this.notifications.error(apiError.message);
      return;
    }

    if (this.syncError() !== apiError.message) {
      this.syncError.set(apiError.message);
      if (apiError.status === 0) {
        this.notifications.warning(apiError.message);
      }
    }
  }

  private markExpired(notify: boolean): void {
    if (this.expired()) {
      return;
    }

    this.stopPolling();
    this.expired.set(true);
    if (notify) {
      this.notifications.warning('This address expired. Generate a new one to keep receiving mail.');
    }
  }

  private startClock(): void {
    if (this.clockHandle !== null) {
      return;
    }

    this.clockHandle = setInterval(() => {
      this.now.set(Date.now());
      if (this.session() && !this.expired() && this.secondsRemaining() === 0) {
        this.markExpired(true);
      }
    }, 1000);
  }

  private applyServerTime(serverTime: string | null | undefined): void {
    const parsed = serverTime ? Date.parse(serverTime) : Number.NaN;
    if (!Number.isNaN(parsed)) {
      this.clockOffsetMs = parsed - Date.now();
    }
  }

  private resetInboxState(): void {
    this.closeMessage();
    this.messages.set([]);
    this.latestOtp.set(null);
    this.otpHistory.set([]);
    this.lastSyncedAt.set(null);
    this.syncError.set(null);
    this.expired.set(false);
    this.initialLoading.set(false);
    this.hasLoadedMessages = false;
  }

  private clearSession(): void {
    this.stopPolling();
    this.resetInboxState();
    this.session.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // Storage unavailable (private mode); nothing to clear.
    }
  }

  private saveSession(session: InboxSession): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      // Storage unavailable: the inbox still works until the page is reloaded.
    }
  }

  private readSavedSession(): InboxSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) {
        return null;
      }
      const parsed = JSON.parse(raw) as Partial<InboxSession>;
      if (typeof parsed.inboxId === 'string' && typeof parsed.accessToken === 'string' && typeof parsed.address === 'string') {
        return {
          inboxId: parsed.inboxId,
          accessToken: parsed.accessToken,
          address: parsed.address,
          createdAt: parsed.createdAt ?? new Date().toISOString(),
          expiresAt: parsed.expiresAt ?? new Date().toISOString(),
          provider: parsed.provider ?? '',
          isDemo: parsed.isDemo ?? false,
          pollIntervalSeconds: parsed.pollIntervalSeconds ?? environment.defaultPollIntervalSeconds,
        };
      }
    } catch {
      // Corrupt or inaccessible storage: start fresh.
    }
    return null;
  }
}
