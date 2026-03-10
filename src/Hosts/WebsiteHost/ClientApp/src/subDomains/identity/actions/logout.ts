import { useActionCommand } from '../../../framework/actions/ActionCommand.ts';
import { EmptyResponse } from '../../../framework/api/apiHost1';
import { EmptyRequest } from '../../../framework/api/EmptyRequest.ts';
import { logout } from '../../../framework/api/websiteHost';
import endUserCacheKeys from '../../endUsers/actions/responseCache.ts';
import organizationCacheKeys from '../../organizations/actions/responseCache.ts';
import userProfileCacheKeys from '../../userProfiles/actions/responseCache.ts';


export const LogoutAction = () =>
  useActionCommand<EmptyRequest, EmptyResponse>({
    request: () => logout(),
    onSuccess: () => window.location.reload(), //so that we pick up the changed auth cookies, and return to dashboard page
    invalidateCacheKeys: [...userProfileCacheKeys.all, ...endUserCacheKeys.all, ...organizationCacheKeys.all]
  });
