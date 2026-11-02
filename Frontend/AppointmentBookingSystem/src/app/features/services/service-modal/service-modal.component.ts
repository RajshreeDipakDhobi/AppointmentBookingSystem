import { Component, EventEmitter, HostListener, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ServiceDto, CreateServiceRequestDto, UpdateServiceRequestDto } from '../../../core/models/service.models';
import { STANDARD_DURATION_OPTIONS, DurationOption, getAvatarGradient } from '../../../core/constants/app.constants';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';
import { ProviderService } from '../../../core/services/provider.service';
import { ServiceProviderDto } from '../../../core/models/provider.models';
import { AppInitialsPipe } from '../../../shared/pipes';

@Component({
  selector: 'app-service-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, AppInitialsPipe],
  templateUrl: './service-modal.component.html',
  styleUrls: ['./service-modal.component.css']
})
export class ServiceModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  private readonly providerService = inject(ProviderService);
  readonly getAvatarGradient = getAvatarGradient;

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() serviceToEdit: ServiceDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ServiceDto>();
  @Output() requestAddProvider = new EventEmitter<void>();

  serviceForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Duration State
  readonly isDurationDropdownOpen = signal<boolean>(false);
  readonly isCustomDuration = signal<boolean>(false);
  readonly durationOptions: DurationOption[] = STANDARD_DURATION_OPTIONS;

  // Specialists / Service Providers State
  readonly availableProviders = signal<ServiceProviderDto[]>([]);
  readonly selectedProviderIds = signal<number[]>([]);
  readonly providerSearchQuery = signal<string>('');
  readonly isProviderDropdownOpen = signal<boolean>(false);
  readonly isLoadingProviders = signal<boolean>(false);

  get selectedDurationLabel(): string {
    const val = Number(this.serviceForm?.get('durationMinutes')?.value) || 30;
    if (this.isCustomDuration()) {
      return `Custom: ${val} Mins (${this.formatDuration(val)})`;
    }
    const match = this.durationOptions.find(o => o.value === val);
    return match ? match.label : `${val} Minutes (${this.formatDuration(val)})`;
  }

  get filteredProviders(): ServiceProviderDto[] {
    const query = this.providerSearchQuery().toLowerCase().trim();
    const list = this.availableProviders();
    if (!query) return list;
    return list.filter(p =>
      (p.name && p.name.toLowerCase().includes(query)) ||
      (p.email && p.email.toLowerCase().includes(query)) ||
      (p.serviceType && p.serviceType.toLowerCase().includes(query))
    );
  }

  get selectedProvidersList(): ServiceProviderDto[] {
    const ids = this.selectedProviderIds();
    return this.availableProviders().filter(p => ids.includes(p.id));
  }

  constructor() {
    this.serviceForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
      description: ['', [Validators.maxLength(1000)]],
      durationMinutes: [30, [Validators.required, Validators.min(5), Validators.max(480)]],
      price: [0, [Validators.required, Validators.min(0)]],
      isActive: [true]
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] || changes['serviceToEdit'] || changes['mode']) {
      if (this.isOpen) {
        this.resetAndInitForm();
      }
    }
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.isSubmitting.set(false);
    this.isDurationDropdownOpen.set(false);
    this.isProviderDropdownOpen.set(false);
    this.providerSearchQuery.set('');

    const initialDuration = this.serviceToEdit?.durationMinutes ?? 30;
    const isStandardPreset = this.durationOptions.some(o => o.value === initialDuration);
    this.isCustomDuration.set(!isStandardPreset && this.mode === 'edit');

    if (this.mode === 'edit' && this.serviceToEdit) {
      this.serviceForm.reset({
        name: this.serviceToEdit.name || '',
        description: this.serviceToEdit.description || '',
        durationMinutes: initialDuration,
        price: this.serviceToEdit.price ?? 0,
        isActive: this.serviceToEdit.isActive ?? true
      });
      this.selectedProviderIds.set(this.serviceToEdit.assignedProviderIds ? [...this.serviceToEdit.assignedProviderIds] : []);
    } else {
      this.serviceForm.reset({
        name: '',
        description: '',
        durationMinutes: 30,
        price: 0,
        isActive: true
      });
      this.selectedProviderIds.set([]);
    }

    this.loadAvailableProviders();
  }

  loadAvailableProviders(): void {
    this.isLoadingProviders.set(true);
    this.providerService.getActiveProviders().subscribe({
      next: (providers) => {
        this.availableProviders.set(providers || []);
        this.isLoadingProviders.set(false);
      },
      error: () => {
        this.isLoadingProviders.set(false);
      }
    });
  }

  // --- Duration Handlers ---
  toggleDurationDropdown(event: Event): void {
    event.stopPropagation();
    this.isDurationDropdownOpen.update(v => !v);
    this.isProviderDropdownOpen.set(false);
  }

  selectDuration(minutes: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.isCustomDuration.set(false);
    this.serviceForm.patchValue({ durationMinutes: minutes });
    this.serviceForm.get('durationMinutes')?.markAsDirty();
    this.serviceForm.get('durationMinutes')?.markAsTouched();
    this.isDurationDropdownOpen.set(false);
  }

  enableCustomDuration(event?: Event): void {
    if (event) event.stopPropagation();
    this.isCustomDuration.set(true);
    this.isDurationDropdownOpen.set(false);
  }

  disableCustomDuration(presetMinutes: number = 30): void {
    this.isCustomDuration.set(false);
    this.serviceForm.patchValue({ durationMinutes: presetMinutes });
    this.serviceForm.get('durationMinutes')?.markAsDirty();
  }

  setCustomDuration(minutes: number): void {
    const clamped = Math.max(5, Math.min(480, Math.round(Number(minutes) || 30)));
    this.serviceForm.patchValue({ durationMinutes: clamped });
    this.serviceForm.get('durationMinutes')?.markAsDirty();
    this.serviceForm.get('durationMinutes')?.markAsTouched();
  }

  stepCustomDuration(delta: number, event?: Event): void {
    if (event) event.stopPropagation();
    const current = Number(this.serviceForm.get('durationMinutes')?.value) || 30;
    this.setCustomDuration(current + delta);
  }

  formatDuration(minutes: number): string {
    const m = Number(minutes) || 0;
    if (m <= 0) return '0 Mins';
    const hrs = Math.floor(m / 60);
    const rem = m % 60;
    if (hrs === 0) return `${rem} Mins`;
    if (rem === 0) return `${hrs} ${hrs === 1 ? 'Hour' : 'Hours'}`;
    return `${hrs} hr ${rem} mins`;
  }

  // --- Specialist / Provider Multi-Selection Handlers ---
  toggleProviderDropdown(event: Event): void {
    event.stopPropagation();
    this.isProviderDropdownOpen.update(v => !v);
    this.isDurationDropdownOpen.set(false);
  }

  toggleProvider(providerId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const current = [...this.selectedProviderIds()];
    const index = current.indexOf(providerId);
    if (index > -1) {
      current.splice(index, 1);
    } else {
      current.push(providerId);
    }
    this.selectedProviderIds.set(current);
  }

  removeProvider(providerId: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedProviderIds.update(ids => ids.filter(id => id !== providerId));
  }

  selectAllFilteredProviders(event?: Event): void {
    if (event) event.stopPropagation();
    const allFilteredIds = this.filteredProviders.map(p => p.id);
    const merged = Array.from(new Set([...this.selectedProviderIds(), ...allFilteredIds]));
    this.selectedProviderIds.set(merged);
  }

  clearAllProviders(event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedProviderIds.set([]);
  }

  onAddProviderClick(event: Event): void {
    event.stopPropagation();
    this.isProviderDropdownOpen.set(false);
    this.requestAddProvider.emit();
  }

  /**
   * Called by parent component when a new provider is created on-the-fly.
   */
  onNewProviderAdded(newProvider: ServiceProviderDto): void {
    const currentList = this.availableProviders();
    if (!currentList.some(p => p.id === newProvider.id)) {
      this.availableProviders.set([...currentList, newProvider]);
    }
    if (!this.selectedProviderIds().includes(newProvider.id)) {
      this.selectedProviderIds.update(ids => [...ids, newProvider.id]);
    }
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isDurationDropdownOpen.set(false);
    this.isProviderDropdownOpen.set(false);
  }

  onClose(): void {
    if (this.isSubmitting()) return;
    this.isDurationDropdownOpen.set(false);
    this.isProviderDropdownOpen.set(false);
    this.close.emit();
  }

  onSubmit(): void {
    if (this.serviceForm.invalid) {
      this.serviceForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const formVal = this.serviceForm.value;

    if (this.mode === 'create') {
      const createDto: CreateServiceRequestDto = {
        name: formVal.name.trim(),
        description: formVal.description ? formVal.description.trim() : undefined,
        durationMinutes: Number(formVal.durationMinutes),
        price: Number(formVal.price),
        isActive: formVal.isActive !== false,
        providerIds: this.selectedProviderIds()
      };

      this.serviceCatalogService.createService(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || err.message || 'Failed to create service.');
        }
      });
    } else {
      if (!this.serviceToEdit) return;

      const updateDto: UpdateServiceRequestDto = {
        name: formVal.name.trim(),
        description: formVal.description ? formVal.description.trim() : undefined,
        durationMinutes: Number(formVal.durationMinutes),
        price: Number(formVal.price),
        isActive: formVal.isActive !== false,
        providerIds: this.selectedProviderIds()
      };

      this.serviceCatalogService.updateService(this.serviceToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || err.message || 'Failed to update service.');
        }
      });
    }
  }
}

