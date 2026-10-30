import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { 
  AppointmentDto, 
  BookAppointmentRequestDto, 
  RescheduleAppointmentRequestDto, 
  CancelAppointmentRequestDto, 
  UpdateAppointmentStatusDto, 
  ProviderDailyAvailabilityResponseDto, 
  AppointmentStatsDto,
  AppointmentStatus
} from '../models/appointment.models';
import { PagedResult, PaginationParams } from '../models/pagination.models';
import { UserDto } from '../models/auth.models';
import { environment } from '../../../environments/environment';

export interface AppointmentFilterParams extends PaginationParams {
  providerId?: number;
  startDate?: string;
  endDate?: string;
  status?: AppointmentStatus;
  search?: string;
  tenantId?: number;
}

@Injectable({
  providedIn: 'root'
})
export class AppointmentService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/appointments`;

  /**
   * Module 3: Get calculated available slots for a provider on a date
   */
  getAvailableSlots(providerId: number, date: string): Observable<ProviderDailyAvailabilityResponseDto> {
    return this.http.get<any>(
      `${this.apiUrl}/available-slots?providerId=${providerId}&date=${date}`
    ).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Urban Company Style: Get merged available slots for all specialists offering a service
   */
  getAvailableSlotsByService(serviceId: number, date: string, tenantId?: number): Observable<ProviderDailyAvailabilityResponseDto> {
    const url = tenantId 
      ? `${this.apiUrl}/service-slots?serviceId=${serviceId}&date=${date}&tenantId=${tenantId}`
      : `${this.apiUrl}/service-slots?serviceId=${serviceId}&date=${date}`;
    return this.http.get<any>(url).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Urban Company Style: Get incoming broadcast requests for specialists
   */
  getIncomingBroadcastRequests(tenantId?: number): Observable<AppointmentDto[]> {
    const url = tenantId ? `${this.apiUrl}/incoming-requests?tenantId=${tenantId}` : `${this.apiUrl}/incoming-requests`;
    return this.http.get<any>(url).pipe(
      map(res => (res && res.data !== undefined) ? res.data : (Array.isArray(res) ? res : []))
    );
  }

  /**
   * Urban Company Style: Specialist accepts an open broadcast appointment request
   */
  acceptBroadcastAppointment(appointmentId: number, providerId?: number): Observable<AppointmentDto> {
    const url = providerId 
      ? `${this.apiUrl}/${appointmentId}/accept?providerId=${providerId}` 
      : `${this.apiUrl}/${appointmentId}/accept`;
    return this.http.post<any>(url, {}).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Module 4: Book an appointment (Unwraps ApiResponse.data)
   */
  bookAppointment(dto: BookAppointmentRequestDto, tenantId?: number): Observable<AppointmentDto> {
    const url = tenantId ? `${this.apiUrl}?tenantId=${tenantId}` : this.apiUrl;
    return this.http.post<any>(url, dto).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Admin: Lookup existing customer by phone number or email (Unwraps ApiResponse.data)
   */
  lookupCustomer(query: string): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/lookup-customer`, {
      params: { query }
    }).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Admin / Service Provider: Multi-match search customers by name, email, or partial phone (Unwraps ApiResponse.data)
   */
  searchCustomers(query: string, tenantId?: number): Observable<UserDto[]> {
    let params = new HttpParams().set('query', query.trim());
    if (tenantId) {
      params = params.set('tenantId', tenantId.toString());
    }
    return this.http.get<any>(`${this.apiUrl}/search-customers`, { params }).pipe(
      map(res => (res && res.data !== undefined) ? res.data : (Array.isArray(res) ? res : []))
    );
  }

  /**
   * Module 7: Get appointments with filters and server-side pagination
   */
  getAppointments(filters?: AppointmentFilterParams): Observable<PagedResult<AppointmentDto>> {
    let params = new HttpParams();

    if (filters) {
      if (filters.providerId) params = params.set('providerId', filters.providerId.toString());
      if (filters.startDate) params = params.set('startDate', filters.startDate);
      if (filters.endDate) params = params.set('endDate', filters.endDate);
      if (filters.status) params = params.set('status', filters.status.toString());
      if (filters.search) params = params.set('search', filters.search);
      if (filters.tenantId) params = params.set('tenantId', filters.tenantId.toString());
      if (filters.pageNumber) params = params.set('pageNumber', filters.pageNumber.toString());
      if (filters.pageSize) params = params.set('pageSize', filters.pageSize.toString());
    }

    return this.http.get<any>(this.apiUrl, { params }).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Get single appointment details
   */
  getAppointmentById(id: number, tenantId?: number): Observable<AppointmentDto> {
    const url = tenantId ? `${this.apiUrl}/${id}?tenantId=${tenantId}` : `${this.apiUrl}/${id}`;
    return this.http.get<any>(url).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Module 5: Reschedule an appointment (Unwraps ApiResponse.data)
   */
  rescheduleAppointment(id: number, dto: RescheduleAppointmentRequestDto, tenantId?: number): Observable<AppointmentDto> {
    const url = tenantId ? `${this.apiUrl}/${id}/reschedule?tenantId=${tenantId}` : `${this.apiUrl}/${id}/reschedule`;
    return this.http.post<any>(url, dto).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Module 5: Cancel an appointment (Unwraps ApiResponse.data)
   */
  cancelAppointment(id: number, dto: CancelAppointmentRequestDto, tenantId?: number): Observable<AppointmentDto> {
    const url = tenantId ? `${this.apiUrl}/${id}/cancel?tenantId=${tenantId}` : `${this.apiUrl}/${id}/cancel`;
    return this.http.post<any>(url, dto).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Update status (e.g. mark Completed) (Unwraps ApiResponse.data)
   */
  updateStatus(id: number, dto: UpdateAppointmentStatusDto, tenantId?: number): Observable<AppointmentDto> {
    const url = tenantId ? `${this.apiUrl}/${id}/status?tenantId=${tenantId}` : `${this.apiUrl}/${id}/status`;
    return this.http.patch<any>(url, dto).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Module 7: Get appointment statistics
   */
  getStats(tenantId?: number): Observable<AppointmentStatsDto> {
    const url = tenantId ? `${this.apiUrl}/stats?tenantId=${tenantId}` : `${this.apiUrl}/stats`;
    return this.http.get<any>(url).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }


  /**
   * Customer Portal: Get all appointments for the logged-in customer with server-side pagination
   */
  getMyAppointments(params?: PaginationParams, status?: AppointmentStatus): Observable<PagedResult<AppointmentDto>> {
    let httpParams = new HttpParams();
    if (params?.pageNumber) httpParams = httpParams.set('pageNumber', params.pageNumber.toString());
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());
    if (params?.search) httpParams = httpParams.set('search', params.search);
    if (status !== undefined && status !== null) httpParams = httpParams.set('status', status.toString());

    return this.http.get<any>(`${this.apiUrl}/my-appointments`, { params: httpParams }).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Customer Portal: Get aggregate statistics for the logged-in customer
   */
  getMyStats(): Observable<AppointmentStatsDto> {
    return this.http.get<any>(`${this.apiUrl}/my-stats`).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }

  /**
   * Customer: Accept or decline a provider's proposed reschedule slot
   */
  respondToProposal(appointmentId: number, accept: boolean): Observable<AppointmentDto> {
    return this.http.post<any>(`${this.apiUrl}/${appointmentId}/respond-proposal?accept=${accept}`, {}).pipe(
      map(res => (res && res.data !== undefined) ? res.data : res)
    );
  }
}
