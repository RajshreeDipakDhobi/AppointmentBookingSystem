import { Component, Input, OnInit, OnDestroy, inject, computed, signal, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { LayoutService } from '../../core/services/layout.service';
import { BookingModalComponent } from '../../features/appointments/booking-modal/booking-modal.component';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule, RouterModule, BookingModalComponent],
  templateUrl: './topbar.component.html',
  styleUrls: ['./topbar.component.css']
})
export class TopbarComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly elementRef = inject(ElementRef);
  readonly layoutService = inject(LayoutService);

  @Input() isPublic: boolean = false;

  readonly currentUser = computed(() => this.authService.currentUser());
  readonly isAuthenticated = computed(() => this.authService.isAuthenticated());
  readonly isCustomer = computed(() => this.authService.isCustomer());
  readonly isAdminOrStaff = computed(() => this.authService.isAdmin() || this.authService.isSuperAdmin());

  readonly isBookingModalOpen = signal<boolean>(false);
  readonly isUserMenuOpen = signal<boolean>(false);

  private bookingSub?: Subscription;

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isUserMenuOpen.set(false);
    }
  }

  toggleUserMenu(event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.isUserMenuOpen.update(v => !v);
  }

  closeUserMenu(): void {
    this.isUserMenuOpen.set(false);
  }

  goToMyAppointments(): void {
    this.closeUserMenu();
    this.router.navigate(['/my-appointments']);
  }

  goToDashboard(): void {
    this.closeUserMenu();
    this.router.navigate(['/dashboard']);
  }

  goToBook(): void {
    this.closeUserMenu();
    this.router.navigate(['/book']);
  }

  ngOnInit(): void {
    this.bookingSub = this.layoutService.openBookingModal$.subscribe(() => {
      this.isBookingModalOpen.set(true);
    });
  }

  ngOnDestroy(): void {
    this.bookingSub?.unsubscribe();
  }

  openBookingModal(): void {
    this.isBookingModalOpen.set(true);
  }

  openLoginDrawer(mode: 'login' | 'register' = 'login'): void {
    if (!this.router.url.includes('/book')) {
      this.router.navigate(['/book'], { queryParams: { auth: mode } }).then(() => {
        this.layoutService.openAuth(mode);
      });
    } else {
      this.layoutService.openAuth(mode);
    }
  }

  get user() {
    return this.currentUser();
  }

  get userFullName(): string {
    if (this.user?.fullName && this.user.fullName.trim().length > 0) {
      return this.user.fullName.trim();
    }
    const fn = (this.user?.firstName || '').trim();
    const ln = (this.user?.lastName || '').trim();
    if (fn || ln) {
      return `${fn} ${ln}`.trim();
    }
    return this.user?.email || 'Account';
  }

  get initials(): string {
    const fn = this.user?.firstName || '';
    const ln = this.user?.lastName || '';
    if (fn && ln) return (fn[0] + ln[0]).toUpperCase();
    if (fn) return fn.slice(0, 2).toUpperCase();
    return 'U';
  }

  get roleBadge(): string {
    return this.user?.roleName || (this.user?.role === 1 ? 'SUPERADMIN' : 'ADMIN');
  }

  onLogout(): void {
    this.closeUserMenu();
    this.authService.logout();
  }
}
