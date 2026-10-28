import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class LayoutService {
  private readonly sidebarOpen = signal<boolean>(false);
  readonly isSidebarOpen = this.sidebarOpen.asReadonly();

  // Observable trigger for opening the unified booking modal from anywhere
  private readonly openBookingModalSource = new Subject<void>();
  readonly openBookingModal$ = this.openBookingModalSource.asObservable();

  triggerOpenBookingModal(): void {
    this.closeSidebar();
    this.openBookingModalSource.next();
  }

  // Observable trigger for opening auth drawer on booking portal
  private readonly openAuthDrawerSource = new Subject<'login' | 'register'>();
  readonly openAuthDrawer$ = this.openAuthDrawerSource.asObservable();

  openAuth(mode: 'login' | 'register' = 'login'): void {
    this.openAuthDrawerSource.next(mode);
  }


  toggleSidebar(): void {
    this.sidebarOpen.update(open => !open);
  }

  openSidebar(): void {
    this.sidebarOpen.set(true);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }
}
