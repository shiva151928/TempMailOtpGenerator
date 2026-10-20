import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { EmailDetailsComponent } from './components/email-details/email-details.component';
import { EmailGeneratorComponent } from './components/email-generator/email-generator.component';
import { InboxComponent } from './components/inbox/inbox.component';
import { NotificationComponent } from './components/notification/notification.component';
import { OtpViewerComponent } from './components/otp-viewer/otp-viewer.component';
import { InboxStore } from './core/inbox.store';

@Component({
  selector: 'app-root',
  imports: [EmailGeneratorComponent, OtpViewerComponent, InboxComponent, EmailDetailsComponent, NotificationComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnInit, OnDestroy {
  protected readonly store = inject(InboxStore);

  ngOnInit(): void {
    this.store.init();
  }

  ngOnDestroy(): void {
    this.store.dispose();
  }
}
