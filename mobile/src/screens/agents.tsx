import { useState } from 'react';
import { FlatList, Pressable, Text, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'expo-router';
import { Stack } from 'expo-router/stack';
import { useApi } from '@/auth/session-provider';
import { Message, QueryState } from '@/components/content';
import { colors, spacing, type } from '@/theme';

export function AgentsScreen() {
  const api = useApi();
  const [search, setSearch] = useState('');
  const query = useQuery({ queryKey: ['agents'], queryFn: ({ signal }) => api.agents(signal) });
  const filtered =
    query.data?.filter((agent) =>
      `${agent.fullName} ${agent.code}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()),
    ) ?? [];
  return (
    <>
      <Stack.SearchBar
        placeholder="Name or agent code"
        onChangeText={(event) => setSearch(event.nativeEvent.text)}
      />
      <FlatList
        data={filtered}
        keyExtractor={(agent) => String(agent.id)}
        contentInsetAdjustmentBehavior="automatic"
        keyboardShouldPersistTaps="handled"
        style={{ backgroundColor: colors.background }}
        contentContainerStyle={{ padding: spacing.md, paddingBottom: spacing.xxl }}
        refreshing={query.isFetching}
        onRefresh={() => {
          void query.refetch();
        }}
        ListHeaderComponent={<QueryState query={query}>{() => null}</QueryState>}
        ListEmptyComponent={
          query.data !== undefined ? (
            <Message title={search ? 'No matching agents' : 'No agents available'}>
              {search
                ? 'Try another name or code.'
                : 'Only agents you may access are shown. Contact your administrator for assignment changes.'}
            </Message>
          ) : undefined
        }
        renderItem={({ item }) => (
          <Link href={{ pathname: '/agent/[id]', params: { id: item.id } }}>
            <Link.Trigger>
              <Pressable
                accessibilityRole="link"
                style={({ pressed }) => ({
                  paddingVertical: spacing.md,
                  opacity: pressed ? 0.6 : 1,
                  borderBottomWidth: 0.5,
                  borderColor: colors.separator,
                })}
              >
                <View style={{ gap: spacing.xs }}>
                  <Text style={type.headline}>{item.fullName || item.code}</Text>
                  <Text style={type.caption}>
                    {item.code} / {item.walletCount} wallets /{' '}
                    {item.isActive ? 'Active' : 'Inactive'}
                  </Text>
                </View>
              </Pressable>
            </Link.Trigger>
            <Link.Preview />
          </Link>
        )}
      />
    </>
  );
}
