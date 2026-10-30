import { Component, OnInit, Input, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { UserRole } from '../../../core/models/auth.models';

import { OnlyDigitsDirective } from '../../../shared/directives';
import { EMAIL_REGEX, PHONE_10_DIGIT_REGEX } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, OnlyDigitsDirective],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  @Input() isDrawer: boolean = false;
  @Input() set initialMode(mode: 'login' | 'register') {
    this.isRegisterMode = (mode === 'register');
    this.modeChange.emit(mode);
  }
  @Output() close = new EventEmitter<void>();
  @Output() authenticated = new EventEmitter<void>();
  @Output() modeChange = new EventEmitter<'login' | 'register'>();

  onClose(): void {
    this.close.emit();
  }

  readonly loginForm: FormGroup = this.fb.group({
    email: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
    password: ['', [Validators.required, Validators.minLength(4)]]
  });

  isRegisterMode = false;
  showPassword = false;
  errorMessage = '';
  isResumingBooking = false;

  readonly registerForm: FormGroup = this.fb.group({
    fullName: ['', [Validators.required, Validators.minLength(2)]],
    phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]],
    email: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
    address: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', [Validators.required, Validators.minLength(6)]]
  });

  get isLoading(): boolean {
    return this.authService.isLoading();
  }

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    const mode = qp.get('mode');
    const resume = qp.get('resumeBooking') === 'true';

    if (mode === 'register') {
      this.isRegisterMode = true;
    }
    this.isResumingBooking = resume;
  }

  toggleMode(): void {
    this.isRegisterMode = !this.isRegisterMode;
    this.errorMessage = '';
    this.modeChange.emit(this.isRegisterMode ? 'register' : 'login');
  }

  setMode(isRegister: boolean): void {
    if (this.isRegisterMode !== isRegister) {
      this.isRegisterMode = isRegister;
      this.errorMessage = '';
      this.modeChange.emit(this.isRegisterMode ? 'register' : 'login');
    }
  }

  onSubmit(): void {
    if (this.isRegisterMode) {
      this.handleRegister();
    } else {
      this.handleLogin();
    }
  }

  private handleLogin(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.errorMessage = '';
    const { email, password } = this.loginForm.value;

    this.authService.login({ email, password }).subscribe({
      next: () => {
        this.redirectAfterAuth();
      },
      error: (err) => {
        this.errorMessage = err.error?.message || err.message || 'Invalid email or password. Please try again.';
      }
    });
  }

  private handleRegister(): void {
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    this.errorMessage = '';
    const val = this.registerForm.value;

    this.authService.register(val).subscribe({
      next: () => {
        this.redirectAfterAuth();
      },
      error: (err) => {
        this.errorMessage = err.error?.message || 'Unable to complete registration. Please check your details.';
      }
    });
  }

  private redirectAfterAuth(): void {
    if (this.isDrawer) {
      this.authenticated.emit();
      return;
    }

    const user = this.authService.currentUser();
    const userRole = user?.role;
    const isCustomer = userRole === UserRole.Customer || (userRole as unknown) === 4 || (userRole as unknown) === '4';

    // 1. If user was in the middle of booking an appointment, resume their booking!
    const pendingDraftRaw = sessionStorage.getItem('pending_booking_draft');
    const isResume = this.isResumingBooking || !!pendingDraftRaw;

    if (isResume && pendingDraftRaw) {
      try {
        const draft = JSON.parse(pendingDraftRaw);
        const targetUrl = draft.returnUrl || this.route.snapshot.queryParamMap.get('returnUrl') || '/booking-portal';
        const separator = targetUrl.includes('?') ? '&' : '?';
        this.router.navigateByUrl(`${targetUrl}${separator}resumeBooking=true`);
        return;
      } catch {
        sessionStorage.removeItem('pending_booking_draft');
      }
    }

    // 2. Normal customer login (no pending draft) -> My Appointments
    if (isCustomer) {
      this.router.navigate(['/my-appointments']);
      return;
    }

    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

    if (returnUrl && !returnUrl.includes('/dashboard') && !returnUrl.includes('/book') && !returnUrl.includes('/booking-portal')) {
      this.router.navigateByUrl(returnUrl);
      return;
    }

    if (userRole === UserRole.ServiceProvider || (userRole as unknown) === 3 || (userRole as unknown) === '3') {
      this.router.navigate(['/appointments']);
      return;
    }

    this.router.navigate(['/dashboard']);
  }

  togglePasswordVisibility(): void {
    this.showPassword = !this.showPassword;
  }
}
