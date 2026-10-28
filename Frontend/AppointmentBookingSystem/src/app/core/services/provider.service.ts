import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { 
  ServiceProviderDto, 
  CreateServiceProviderRequestDto, 
  UpdateServiceProviderRequestDto, 
  ProviderStatus 
} from '../models/provider.models';
import { PagedResult, PaginationParams } from '../models/pagination.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ProviderService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/providers`;

  getProvidersPaged(params?: PaginationParams, status?: ProviderStatus, tenantId?: number): Observable<PagedResult<ServiceProviderDto>> {
    let httpParams = new HttpParams();
    if (tenantId) httpParams = httpParams.set('tenantId', tenantId);
    if (params?.pageNumber) httpParams = httpParams.set('pageNumber', params.pageNumber);
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize);
    if (params?.search) httpParams = httpParams.set('search', params.search);
    if (status !== undefined && status !== null) httpParams = httpParams.set('status', status);

    return this.http.get<PagedResult<ServiceProviderDto>>(this.apiUrl, { params: httpParams });
  }

  getAllProviders(tenantId?: number): Observable<ServiceProviderDto[]> {
    return this.getActiveProviders(tenantId);
  }

  getActiveProviders(tenantId?: number): Observable<ServiceProviderDto[]> {
    const url = tenantId ? `${this.apiUrl}/active?tenantId=${tenantId}` : `${this.apiUrl}/active`;
    return this.http.get<ServiceProviderDto[]>(url);
  }

  getProviderById(id: number, tenantId?: number): Observable<ServiceProviderDto> {
    const url = tenantId ? `${this.apiUrl}/${id}?tenantId=${tenantId}` : `${this.apiUrl}/${id}`;
    return this.http.get<ServiceProviderDto>(url);
  }

  createProvider(dto: CreateServiceProviderRequestDto, tenantId?: number): Observable<ServiceProviderDto> {
    const url = tenantId ? `${this.apiUrl}?tenantId=${tenantId}` : this.apiUrl;
    return this.http.post<ServiceProviderDto>(url, dto);
  }

  updateProvider(id: number, dto: UpdateServiceProviderRequestDto, tenantId?: number): Observable<ServiceProviderDto> {
    const url = tenantId ? `${this.apiUrl}/${id}?tenantId=${tenantId}` : `${this.apiUrl}/${id}`;
    return this.http.put<ServiceProviderDto>(url, dto);
  }

  updateProviderStatus(id: number, status: ProviderStatus, tenantId?: number): Observable<{ message: string }> {
    const url = tenantId ? `${this.apiUrl}/${id}/status?tenantId=${tenantId}` : `${this.apiUrl}/${id}/status`;
    return this.http.put<{ message: string }>(url, status);
  }

  deleteProvider(id: number, tenantId?: number): Observable<{ message: string }> {
    const url = tenantId ? `${this.apiUrl}/${id}?tenantId=${tenantId}` : `${this.apiUrl}/${id}`;
    return this.http.delete<{ message: string }>(url);
  }

  uploadAvatar(file: File): Observable<{ url: string; relativeUrl: string; fileName: string; size: number; message: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ url: string; relativeUrl: string; fileName: string; size: number; message: string }>(
      `${this.apiUrl}/upload-avatar`,
      formData
    );
  }

  uploadProviderAvatar(providerId: number, file: File, tenantId?: number): Observable<ServiceProviderDto> {
    const formData = new FormData();
    formData.append('file', file);
    const url = tenantId ? `${this.apiUrl}/${providerId}/avatar?tenantId=${tenantId}` : `${this.apiUrl}/${providerId}/avatar`;
    return this.http.post<ServiceProviderDto>(url, formData);
  }
}
