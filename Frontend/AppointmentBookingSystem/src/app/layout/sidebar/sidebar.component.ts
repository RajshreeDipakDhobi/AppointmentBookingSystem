import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { LayoutService } from '../../core/services/layout.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  readonly layoutService = inject(LayoutService);

  get user() {
    return this.authService.currentUser();
  }

  get isSuperAdmin(): boolean {
    return this.user?.role === 1; // UserRole.SuperAdmin
  }

  get isTenantAdmin(): boolean {
    return this.user?.role === 2; // UserRole.Admin
  }

  get isServiceProvider(): boolean {
    return this.user?.role === 3; // UserRole.ServiceProvider
  }

  get isCustomer(): boolean {
    return this.user?.role === 4; // UserRole.Customer
  }
}
