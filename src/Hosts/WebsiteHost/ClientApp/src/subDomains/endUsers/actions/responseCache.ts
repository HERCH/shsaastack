const endUserCacheKeys = {
  all: ['users'] as const,
  memberships: {
    all: ['users', 'memberships'] as const,
    me: ['users', 'memberships', 'me'] as const,
    mutate: (userId: string) => [...endUserCacheKeys.memberships.all, userId] as const
  },
  users: {
    all: ['users'] as const,
    me: ['users', 'me'] as const
  }
};

export default endUserCacheKeys;
