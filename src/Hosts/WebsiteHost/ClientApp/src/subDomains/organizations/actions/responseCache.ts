const organizationCacheKeys = {
  all: ['organizations'] as const,
  organization: {
    query: (organizationId: string) => [...organizationCacheKeys.all, organizationId] as const,
    mutate: (organizationId: string) => [...organizationCacheKeys.all, organizationId] as const,
    members: {
      all: ['organizations', 'members'] as const,
      query: (organizationId: string) => [...organizationCacheKeys.organization.members.all, organizationId] as const,
      mutate: (organizationId: string) => [...organizationCacheKeys.organization.members.all, organizationId] as const
    }
  }
};

export default organizationCacheKeys;
