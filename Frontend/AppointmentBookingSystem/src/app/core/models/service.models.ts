export interface ServiceSpecialistDto {
  id: number;
  name: string;
  email: string;
  profilePictureUrl?: string;
}

export interface ServiceDto {
  id: number;
  userId: number;
  createdByName?: string;
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive: boolean;
  providersCount: number;
  assignedProviders?: ServiceSpecialistDto[];
  assignedProviderIds?: number[];
  assignedProviderNames?: string[];
  createdAt: string;
}

export interface CreateServiceRequestDto {
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive?: boolean;
  providerIds?: number[];
}

export interface UpdateServiceRequestDto {
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive: boolean;
  providerIds?: number[];
}

export type { DurationOption } from '../constants/app.constants';
export { STANDARD_DURATION_OPTIONS } from '../constants/app.constants';

export interface ProviderTodayBookingDto {
  appointmentId: number;
  appointmentNumber: string;
  customerName: string;
  customerPhone?: string;
  serviceId: number;
  serviceName: string;
  startTime: string;
  endTime: string;
  timeRange: string;
  durationMinutes: number;
  status: string;
  isOngoingNow: boolean;
  isUpcoming: boolean;
  isCompleted: boolean;
  progressOrRemainingNote: string;
}

export interface ProviderAssignedServiceItemDto {
  id: number;
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive: boolean;
  todayBookingsCount: number;
  allTimeBookingsCount: number;
}

export interface ProviderServicesOverviewDto {
  providerId: number;
  providerName: string;
  email: string;
  phoneNumber: string;
  profilePictureUrl?: string;
  specialization: string;
  chargesAmount: number;
  todayDateFormatted: string;
  dayName: string;
  isWorkingToday: boolean;
  workingHours: string;
  breakHours?: string;
  currentWorkStatus: 'Available' | 'Busy' | 'OnBreak' | 'ShiftEnded' | 'NotStarted' | 'OffToday';
  currentStatusNote: string;
  todayAppointmentsCount: number;
  completedTodayCount: number;
  remainingTodayCount: number;
  todayTimeline: ProviderTodayBookingDto[];
  totalServicesCount: number;
  services: ProviderAssignedServiceItemDto[];
}


