import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TenantDto, CreateTenantRequestDto, UpdateTenantRequestDto } from '../../../core/models/tenant.models';
import { TenantService } from '../../../core/services/tenant.service';

import { OnlyDigitsDirective } from '../../../shared/directives';
import { EMAIL_REGEX, PHONE_10_DIGIT_REGEX, filter10Digits } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-tenant-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, OnlyDigitsDirective],
  templateUrl: './tenant-modal.component.html',
  styleUrls: ['./tenant-modal.component.css']
})
export class TenantModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly tenantService = inject(TenantService);

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() tenantToEdit: TenantDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<TenantDto>();

  tenantForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly showPassword = signal<boolean>(false);

  private isTenantCodeManual = false;

  constructor() {
    this.tenantForm = this.fb.group({
      // Organization fields (All Required)
      tenantName: ['', [Validators.required, Validators.minLength(2)]],
      tenantCode: ['', [Validators.required, Validators.minLength(2), Validators.pattern(/^[a-z0-9\-]+$/i)]],
      contactEmail: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
      contactPhone: ['', [Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]],
      address: ['', [Validators.required]],

      // Admin Account fields (All Required)
      adminFirstName: ['', [Validators.required]],
      adminLastName: ['', [Validators.required]],
      adminEmail: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
      adminPassword: ['', [Validators.required, Validators.minLength(6)]]
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen) {
      this.resetAndInitForm();
    }
  }

  onTenantNameInput(): void {
    if (this.mode === 'create' && !this.isTenantCodeManual) {
      const name = this.tenantForm.get('tenantName')?.value || '';
      const slug = name.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
      this.tenantForm.get('tenantCode')?.setValue(slug, { emitEvent: false });
    }
  }

  onTenantCodeInput(): void {
    this.isTenantCodeManual = true;
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.isSubmitting.set(false);
    this.showPassword.set(false);

    if (this.mode === 'edit' && this.tenantToEdit) {
      this.isTenantCodeManual = true;
      // Edit mode: populate organization fields AND admin fields
      this.tenantForm.reset({
        tenantName: this.tenantToEdit.name,
        tenantCode: this.tenantToEdit.tenantCode || this.tenantToEdit.slug || '',
        contactEmail: this.tenantToEdit.contactEmail || '',
        contactPhone: filter10Digits(this.tenantToEdit.contactPhone || ''),
        address: this.tenantToEdit.address || '',
        adminFirstName: this.tenantToEdit.adminFirstName || '',
        adminLastName: this.tenantToEdit.adminLastName || '',
        adminEmail: this.tenantToEdit.adminEmail || '',
        adminPassword: ''
      });

      this.tenantForm.get('tenantName')?.setValidators([Validators.required, Validators.minLength(2)]);
      this.tenantForm.get('tenantCode')?.setValidators([Validators.required, Validators.minLength(2), Validators.pattern(/^[a-z0-9\-]+$/i)]);
      this.tenantForm.get('contactEmail')?.setValidators([Validators.required, Validators.pattern(EMAIL_REGEX)]);
      this.tenantForm.get('contactPhone')?.setValidators([Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]);
      this.tenantForm.get('address')?.setValidators([Validators.required]);
      this.tenantForm.get('adminFirstName')?.setValidators([Validators.required]);
      this.tenantForm.get('adminLastName')?.setValidators([Validators.required]);
      this.tenantForm.get('adminEmail')?.setValidators([Validators.required, Validators.pattern(EMAIL_REGEX)]);
      this.tenantForm.get('adminPassword')?.setValidators([Validators.minLength(6)]);
    } else {
      this.isTenantCodeManual = false;
      // Create mode: all fields required
      this.tenantForm.reset({
        tenantName: '',
        tenantCode: '',
        contactEmail: '',
        contactPhone: '',
        address: '',
        adminFirstName: '',
        adminLastName: '',
        adminEmail: '',
        adminPassword: ''
      });

      this.tenantForm.get('tenantName')?.setValidators([Validators.required, Validators.minLength(2)]);
      this.tenantForm.get('tenantCode')?.setValidators([Validators.required, Validators.minLength(2), Validators.pattern(/^[a-z0-9\-]+$/i)]);
      this.tenantForm.get('contactEmail')?.setValidators([Validators.required, Validators.pattern(EMAIL_REGEX)]);
      this.tenantForm.get('contactPhone')?.setValidators([Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]);
      this.tenantForm.get('address')?.setValidators([Validators.required]);
      this.tenantForm.get('adminFirstName')?.setValidators([Validators.required]);
      this.tenantForm.get('adminLastName')?.setValidators([Validators.required]);
      this.tenantForm.get('adminEmail')?.setValidators([Validators.required, Validators.pattern(EMAIL_REGEX)]);
      this.tenantForm.get('adminPassword')?.setValidators([Validators.required, Validators.minLength(6)]);
    }

    this.tenantForm.get('tenantName')?.updateValueAndValidity();
    this.tenantForm.get('tenantCode')?.updateValueAndValidity();
    this.tenantForm.get('contactEmail')?.updateValueAndValidity();
    this.tenantForm.get('contactPhone')?.updateValueAndValidity();
    this.tenantForm.get('address')?.updateValueAndValidity();
    this.tenantForm.get('adminFirstName')?.updateValueAndValidity();
    this.tenantForm.get('adminLastName')?.updateValueAndValidity();
    this.tenantForm.get('adminEmail')?.updateValueAndValidity();
    this.tenantForm.get('adminPassword')?.updateValueAndValidity();
  }

  togglePasswordVisibility(): void {
    this.showPassword.update(show => !show);
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
      this.onClose();
    }
  }

  onClose(): void {
    this.errorMessage.set(null);
    this.close.emit();
  }

  onSubmit(): void {
    if (this.tenantForm.invalid) {
      this.tenantForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.tenantForm.value;

    if (this.mode === 'create') {
      const createDto: CreateTenantRequestDto = {
        tenantName: val.tenantName.trim(),
        tenantCode: val.tenantCode ? val.tenantCode.trim().toLowerCase() : undefined,
        contactEmail: val.contactEmail?.trim() || undefined,
        contactPhone: val.contactPhone ? val.contactPhone.trim() : undefined,
        address: val.address ? val.address.trim() : undefined,
        adminFirstName: val.adminFirstName.trim(),
        adminLastName: val.adminLastName.trim(),
        adminEmail: val.adminEmail.trim(),
        adminPassword: val.adminPassword
      };

      this.tenantService.createTenant(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to create organization. Please try again.');
        }
      });
    } else if (this.mode === 'edit' && this.tenantToEdit) {
      const updateDto: UpdateTenantRequestDto = {
        tenantName: val.tenantName.trim(),
        tenantCode: val.tenantCode ? val.tenantCode.trim().toLowerCase() : undefined,
        contactEmail: val.contactEmail?.trim() || undefined,
        contactPhone: val.contactPhone ? val.contactPhone.trim() : undefined,
        address: val.address ? val.address.trim() : undefined,
        adminFirstName: val.adminFirstName ? val.adminFirstName.trim() : undefined,
        adminLastName: val.adminLastName ? val.adminLastName.trim() : undefined,
        adminEmail: val.adminEmail ? val.adminEmail.trim() : undefined,
        adminPassword: val.adminPassword && val.adminPassword.trim().length > 0 ? val.adminPassword : undefined
      };

      this.tenantService.updateTenant(this.tenantToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to update organization. Please try again.');
        }
      });
    }
  }
}
