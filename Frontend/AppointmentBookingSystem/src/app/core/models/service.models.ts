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
  createdAt: string;
}

export interface CreateServiceRequestDto {
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive?: boolean;
}

export interface UpdateServiceRequestDto {
  name: string;
  description?: string;
  durationMinutes: number;
  price: number;
  isActive: boolean;
}

export type { DurationOption } from '../constants/app.constants';
export { STANDARD_DURATION_OPTIONS } from '../constants/app.constants';

