import { Pipe, PipeTransform } from '@angular/core';
import { getInitials } from '../../core/constants/app.constants';

@Pipe({
  name: 'appInitials',
  standalone: true
})
export class AppInitialsPipe implements PipeTransform {
  transform(name: string | null | undefined): string {
    return getInitials(name);
  }
}
