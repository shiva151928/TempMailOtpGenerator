import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ClipboardService } from '../../core/clipboard.service';
import { InboxStore } from '../../core/inbox.store';
import { NotificationService } from '../../core/notification.service';
import { formatCountdown, relativeTime } from '../../core/time-format';

@Component({
  selector: 'app-email-generator',
  templateUrl: './email-generator.component.html',
  styleUrl: './email-generator.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailGeneratorComponent {
  protected readonly store = inject(InboxStore);
  private readonly clipboard = inject(ClipboardService);
  private readonly notifications = inject(NotificationService);

  protected readonly copied = signal(false);
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly countdown = computed(() => formatCountdown(this.store.secondsRemaining()));
  protected readonly percentLeft = computed(() => {
    const lifetime = this.store.lifetimeSeconds();
    return lifetime > 0 ? Math.min(100, Math.max(0, (this.store.secondsRemaining() / lifetime) * 100)) : 0;
  });
  protected readonly percentRounded = computed(() => Math.round(this.percentLeft()));
  protected readonly expiringSoon = computed(
    () => !this.store.expired() && this.store.hasInbox() && this.store.secondsRemaining() <= 300,
  );
  protected readonly lastChecked = computed(() => relativeTime(this.store.lastSyncedAt(), this.store.now()));

  protected async copyAddress(address: string): Promise<void> {
    const ok = await this.clipboard.copy(address);
    if (ok) {
      this.copied.set(true);
      this.notifications.success('Email address copied.');
      if (this.copiedTimer !== null) {
        clearTimeout(this.copiedTimer);
      }
      this.copiedTimer = setTimeout(() => this.copied.set(false), 2000);
    } else {
      this.notifications.error("Couldn't copy automatically. Select the address and copy it manually.");
    }
  }

  protected selectAll(event: Event): void {
    (event.target as HTMLInputElement).select();
  }

  protected confirmDelete(): void {
    if (window.confirm('Delete this inbox and all of its messages? This cannot be undone.')) {
      this.store.deleteInbox();
    }
  }
}
