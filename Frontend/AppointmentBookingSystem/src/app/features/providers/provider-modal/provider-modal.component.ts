import { Component, EventEmitter, HostListener, Input, OnChanges, Output, SimpleChanges, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ServiceProviderDto, CreateServiceProviderRequestDto, UpdateServiceProviderRequestDto } from '../../../core/models/provider.models';
import { ServiceDto } from '../../../core/models/service.models';
import { ProviderService } from '../../../core/services/provider.service';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';

import { OnlyDigitsDirective } from '../../../shared/directives';
import { AppInitialsPipe } from '../../../shared/pipes';
import { EMAIL_REGEX, PHONE_10_DIGIT_REGEX, filter10Digits, getAvatarGradient } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-provider-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, OnlyDigitsDirective, AppInitialsPipe],
  templateUrl: './provider-modal.component.html',
  styleUrls: ['./provider-modal.component.css']
})
export class ProviderModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly providerService = inject(ProviderService);
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  readonly getAvatarGradient = getAvatarGradient;

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() providerToEdit: ServiceProviderDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ServiceProviderDto>();
  @Output() requestAddService = new EventEmitter<void>();

  providerForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly previewAvatarUrl = signal<string | null>(null);
  readonly isUploadingAvatar = signal<boolean>(false);
  readonly avatarError = signal<string | null>(null);

  readonly availableServices = signal<ServiceDto[]>([]);
  readonly selectedServiceIds = signal<number[]>([]);
  readonly selectedService = signal<ServiceDto | null>(null);
  readonly isServiceDropdownOpen = signal<boolean>(false);
  readonly serviceSearchQuery = signal<string>('');

  // Weekly Working Days & Schedule Setup
  readonly weekDaysList = [
    { day: 1, name: 'Monday', short: 'Mon' },
    { day: 2, name: 'Tuesday', short: 'Tue' },
    { day: 3, name: 'Wednesday', short: 'Wed' },
    { day: 4, name: 'Thursday', short: 'Thu' },
    { day: 5, name: 'Friday', short: 'Fri' },
    { day: 6, name: 'Saturday', short: 'Sat' },
    { day: 0, name: 'Sunday', short: 'Sun' }
  ];
  readonly workingDays = signal<number[]>([1, 2, 3, 4, 5, 6, 0]); // All 7 days active by default, click any day to mark Off

  readonly filteredServices = computed(() => {
    const q = this.serviceSearchQuery().trim().toLowerCase();
    const list = this.availableServices();
    if (!q) return list;
    return list.filter(s => 
      s.name.toLowerCase().includes(q) || 
      (s.description && s.description.toLowerCase().includes(q))
    );
  });

  get selectedServicesList(): ServiceDto[] {
    const ids = this.selectedServiceIds();
    return this.availableServices().filter(s => ids.includes(s.id));
  }

  constructor() {
    this.providerForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]],
      serviceId: [null],
      serviceType: [''],
      email: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
      phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]],
      chargesAmount: [0],
      address: [''],
      bio: ['', [Validators.required, Validators.minLength(5)]],
      profilePictureUrl: ['']
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] || changes['providerToEdit'] || changes['mode']) {
      if (this.isOpen) {
        this.loadActiveServices();
        this.resetAndInitForm();
      }
    }
  }

  private loadActiveServices(): void {
    const tenantId = this.providerToEdit?.tenantId;
    this.serviceCatalogService.getActiveServices(tenantId).subscribe({
      next: (services) => {
        this.availableServices.set(services || []);
        this.syncSelectedServicesFromProvider();
      },
      error: () => {
        // Non-blocking fallback
      }
    });
  }

  private syncSelectedServicesFromProvider(): void {
    const services = this.availableServices();
    if (services.length === 0) return;

    if (this.providerToEdit) {
      let idsToSelect: number[] = [];

      if (this.providerToEdit.serviceIds && this.providerToEdit.serviceIds.length > 0) {
        idsToSelect = this.providerToEdit.serviceIds.filter(id => services.some(s => s.id === id));
      } else if (this.providerToEdit.serviceId && services.some(s => s.id === this.providerToEdit!.serviceId)) {
        idsToSelect = [this.providerToEdit.serviceId];
      } else if (this.providerToEdit.serviceType) {
        const match = services.find(s => s.name.toLowerCase() === this.providerToEdit!.serviceType.toLowerCase());
        if (match) {
          idsToSelect = [match.id];
        }
      }

      this.selectedServiceIds.set(idsToSelect);
      this.syncFormWithSelectedServices();
    } else {
      const currentIds = this.selectedServiceIds();
      if (currentIds.length > 0) {
        this.syncFormWithSelectedServices();
      }
    }
  }

  toggleServiceDropdown(event: Event): void {
    event.stopPropagation();
    this.isServiceDropdownOpen.update(v => !v);
  }

  toggleService(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const current = [...this.selectedServiceIds()];
    const index = current.indexOf(serviceId);
    if (index > -1) {
      current.splice(index, 1);
    } else {
      current.push(serviceId);
    }
    this.selectedServiceIds.set(current);
    this.syncFormWithSelectedServices();
  }

  removeService(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedServiceIds.update(ids => ids.filter(id => id !== serviceId));
    this.syncFormWithSelectedServices();
  }

  selectAllFilteredServices(event?: Event): void {
    if (event) event.stopPropagation();
    const filteredIds = this.filteredServices().map(s => s.id);
    const merged = Array.from(new Set([...this.selectedServiceIds(), ...filteredIds]));
    this.selectedServiceIds.set(merged);
    this.syncFormWithSelectedServices();
  }

  clearAllServices(event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedServiceIds.set([]);
    this.syncFormWithSelectedServices();
  }

  onAddServiceClick(event: Event): void {
    event.stopPropagation();
    this.isServiceDropdownOpen.set(false);
    this.requestAddService.emit();
  }

  /**
   * Called by parent component when a new service is created on-the-fly.
   */
  onNewServiceAdded(newService: ServiceDto): void {
    const currentList = this.availableServices();
    if (!currentList.some(s => s.id === newService.id)) {
      this.availableServices.set([...currentList, newService]);
    }
    if (!this.selectedServiceIds().includes(newService.id)) {
      this.selectedServiceIds.update(ids => [...ids, newService.id]);
    }
    this.syncFormWithSelectedServices();
  }

  private syncFormWithSelectedServices(): void {
    const ids = this.selectedServiceIds();
    const list = this.availableServices().filter(s => ids.includes(s.id));
    const primary = list[0] || null;

    this.selectedService.set(primary);
    this.providerForm.patchValue({
      serviceId: primary?.id ?? null,
      serviceType: list.map(s => s.name).join(', '),
      chargesAmount: primary?.price ?? 0
    });
    this.providerForm.get('serviceId')?.markAsDirty();
    this.providerForm.get('serviceId')?.markAsTouched();
  }

  onServiceSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value || '';
    this.serviceSearchQuery.set(val);
  }

  clearServiceSearch(event: Event): void {
    event.stopPropagation();
    this.serviceSearchQuery.set('');
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isServiceDropdownOpen.set(false);
  }

  toggleWorkingDay(day: number): void {
    const current = [...this.workingDays()];
    const idx = current.indexOf(day);
    if (idx > -1) {
      if (current.length > 1) {
        current.splice(idx, 1); // Mark Day Off
      }
    } else {
      current.push(day); // Mark Working Day
    }
    this.workingDays.set(current);
  }

  isDayWorking(day: number): boolean {
    return this.workingDays().includes(day);
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.avatarError.set(null);
    this.isSubmitting.set(false);
    this.isUploadingAvatar.set(false);
    this.selectedService.set(null);
    this.isServiceDropdownOpen.set(false);
    this.serviceSearchQuery.set('');
    this.workingDays.set([1, 2, 3, 4, 5, 6, 0]);

    if (this.mode === 'edit' && this.providerToEdit) {
      this.previewAvatarUrl.set(this.providerToEdit.profilePictureUrl || null);
      this.providerForm.reset({
        name: this.providerToEdit.name || '',
        serviceId: this.providerToEdit.serviceId || null,
        serviceType: this.providerToEdit.serviceType || '',
        email: this.providerToEdit.email || '',
        phoneNumber: filter10Digits(this.providerToEdit.phoneNumber) || '',
        chargesAmount: this.providerToEdit.chargesAmount ?? 0,
        address: this.providerToEdit.address || '',
        bio: this.providerToEdit.bio || '',
        profilePictureUrl: this.providerToEdit.profilePictureUrl || ''
      });
      this.syncSelectedServicesFromProvider();
    } else {
      this.previewAvatarUrl.set(null);
      this.selectedServiceIds.set([]);
      this.providerForm.reset({
        name: '',
        serviceId: null,
        serviceType: '',
        email: '',
        phoneNumber: '',
        chargesAmount: 0,
        address: '',
        bio: '',
        profilePictureUrl: ''
      });
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    this.avatarError.set(null);

    // Limit to 5 MB
    if (file.size > 5 * 1024 * 1024) {
      this.avatarError.set('Image size exceeds 5 MB. Please select a smaller photo.');
      input.value = '';
      return;
    }

    // Validate type
    const validTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
    if (!validTypes.includes(file.type)) {
      this.avatarError.set('Invalid image format. Allowed formats: JPG, PNG, WEBP, GIF.');
      input.value = '';
      return;
    }

    // Set immediate client-side preview
    const reader = new FileReader();
    reader.onload = () => {
      this.previewAvatarUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);

    // Upload to server storage
    this.isUploadingAvatar.set(true);
    this.providerService.uploadAvatar(file).subscribe({
      next: (res: any) => {
        this.isUploadingAvatar.set(false);
        const url = res?.url || res?.data?.url || (typeof res === 'string' ? res : '');
        if (url) {
          this.providerForm.patchValue({ profilePictureUrl: url });
        }
      },
      error: (err) => {
        this.isUploadingAvatar.set(false);
        this.avatarError.set(err?.error?.message || 'Failed to upload photo. Please try again.');
      }
    });
  }

  onAvatarImgError(): void {
    this.previewAvatarUrl.set(null);
  }

  removeAvatar(): void {
    this.previewAvatarUrl.set(null);
    this.avatarError.set(null);
    this.providerForm.patchValue({ profilePictureUrl: '' });
  }

  onClose(): void {
    this.errorMessage.set(null);
    this.avatarError.set(null);
    this.isServiceDropdownOpen.set(false);
    this.serviceSearchQuery.set('');
    this.close.emit();
  }

  onSubmit(): void {
    if (this.selectedServiceIds().length === 0) {
      this.errorMessage.set('Please select at least one assigned service.');
      this.providerForm.markAllAsTouched();
      return;
    }

    if (this.providerForm.invalid || this.isUploadingAvatar()) {
      this.providerForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.providerForm.value;
    const photoUrl = val.profilePictureUrl && val.profilePictureUrl.trim().length > 0 ? val.profilePictureUrl.trim() : undefined;
    const serviceIds = this.selectedServiceIds();
    const primaryServiceId = serviceIds[0];

    if (this.mode === 'create') {
      const allDays = [0, 1, 2, 3, 4, 5, 6];
      const offDays = allDays.filter(d => !this.workingDays().includes(d));

      const createDto: CreateServiceProviderRequestDto = {
        name: val.name.trim(),
        serviceId: primaryServiceId,
        serviceIds: serviceIds,
        serviceType: val.serviceType ? val.serviceType.trim() : undefined,
        email: val.email.trim(),
        phoneNumber: val.phoneNumber.trim(),
        chargesAmount: val.chargesAmount != null ? Number(val.chargesAmount) : 0,
        address: val.address ? val.address.trim() : undefined,
        bio: val.bio ? val.bio.trim() : undefined,
        profilePictureUrl: photoUrl,
        offDays: offDays,
        startTime: '10:00',
        endTime: '17:00',
        breakStartTime: '13:00',
        breakEndTime: '14:00'
      };

      this.providerService.createProvider(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to add service provider. Please try again.');
        }
      });
    } else if (this.mode === 'edit' && this.providerToEdit) {
      const updateDto: UpdateServiceProviderRequestDto = {
        name: val.name.trim(),
        serviceId: primaryServiceId,
        serviceIds: serviceIds,
        serviceType: val.serviceType ? val.serviceType.trim() : undefined,
        email: val.email.trim(),
        phoneNumber: val.phoneNumber.trim(),
        chargesAmount: val.chargesAmount != null ? Number(val.chargesAmount) : 0,
        address: val.address ? val.address.trim() : undefined,
        bio: val.bio ? val.bio.trim() : undefined,
        profilePictureUrl: photoUrl,
        status: this.providerToEdit.status
      };

      this.providerService.updateProvider(this.providerToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to update provider. Please try again.');
        }
      });
    }
  }
}
