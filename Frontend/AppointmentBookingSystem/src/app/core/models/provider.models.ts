export enum ProviderStatus {
  Active = 1,
  Inactive = 2
}

export interface ProviderServiceSummaryDto {
  id: number;
  name: string;
  durationMinutes: number;
  price: number;
}

export interface ServiceProviderDto {
  id: number;
  tenantId: number;
  userId?: number;
  name: string;
  serviceId?: number;
  serviceName?: string;
  serviceIds?: number[];
  serviceNames?: string[];
  assignedServices?: ProviderServiceSummaryDto[];
  serviceType: string;
  email: string;
  phoneNumber: string;
  address?: string;
  profilePictureUrl?: string;
  bio?: string;
  chargesAmount?: number;
  status: ProviderStatus;
  statusName: string;
  createdAt: string;
  totalAppointmentsCount: number;
}

export interface CreateServiceProviderRequestDto {
  name: string;
  serviceId?: number;
  serviceIds?: number[];
  serviceType?: string;
  email: string;
  phoneNumber: string;
  chargesAmount?: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
  offDays?: number[];
  startTime?: string;
  endTime?: string;
  breakStartTime?: string;
  breakEndTime?: string;
  slotDurationMinutes?: number;
}

export interface UpdateServiceProviderRequestDto {
  name: string;
  serviceId?: number;
  serviceIds?: number[];
  serviceType?: string;
  email: string;
  phoneNumber: string;
  chargesAmount?: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
  status: ProviderStatus;
}
