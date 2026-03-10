const oAuth2CacheKeys = {
  all: ['oauth2'] as const,
  client: {
    all: ['oauth2', 'clients'] as const,
    query: (clientId: string) => [...oAuth2CacheKeys.client.all, clientId] as const,
    mutate: (clientId: string) => [...oAuth2CacheKeys.client.all, clientId] as const,
    consent: {
      all: ['oauth2', 'clients', 'consents'] as const,
      query: (clientId: string) => [...oAuth2CacheKeys.client.consent.all, clientId] as const,
      mutate: (clientId: string) => [...oAuth2CacheKeys.client.consent.all, clientId] as const
    }
  }
};

export default oAuth2CacheKeys;
