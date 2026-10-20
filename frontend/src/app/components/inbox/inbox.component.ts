import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { InboxStore } from '../../core/inbox.store';
import { relativeTime } from '../../core/time-format';

@Component({
  selector: 'app-inbox',
  templateUrl: './inbox.component.html',
  styleUrl: './inbox.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InboxComponent {
  protected readonly store = inject(InboxStore);
  protected readonly skeletonRows = [0, 1, 2];

  protected relative(iso: string): string {
    return relativeTime(iso, this.store.now());
  }
}
