const userProfileCacheKeys = {
  all: ['userProfiles'] as const,
  me: ['userProfiles', 'me'] as const,
  profile: {
    mutate: (userId: string) => [...userProfileCacheKeys.all, userId] as const
  }
};

export default userProfileCacheKeys;
