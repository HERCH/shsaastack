import { useActionQuery } from '../../../framework/actions/ActionQuery';
import {
  getOrganization,
  GetOrganizationData,
  GetOrganizationResponse,
  Organization
} from '../../../framework/api/apiHost1';
import organizationCacheKeys from './responseCache';


export enum OrganizationErrorCodes {
  forbidden = 'forbidden'
}

export const GetOrganizationAction = () =>
  useActionQuery<GetOrganizationData, GetOrganizationResponse, Organization, OrganizationErrorCodes>({
    request: (request) => getOrganization(request),
    transform: (res) => res.organization,
    passThroughErrors: {
      403: OrganizationErrorCodes.forbidden
    },
    cacheKey: (request) => organizationCacheKeys.organization.query(request.path.Id)
  });
