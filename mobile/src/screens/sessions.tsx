import { FlatList, Pressable, Text } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'expo-router';
import { useApi } from '@/auth/session-provider';
import { Message, QueryState } from '@/components/content';
import { sessionStatus } from '@/utils/format';
import { colors, spacing, type } from '@/theme';

export function SessionsScreen() {
  const api = useApi();
  const query = useQuery({ queryKey: ['sessions'], queryFn: ({ signal }) => api.sessions(signal) });
  return (
    <FlatList
      data={query.data ?? []}
      keyExtractor={(item) => String(item.id)}
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
          <Message title="No cash sessions">
            Sessions appear here after your branch starts a count. Agents see only their own
            participation.
          </Message>
        ) : undefined
      }
      renderItem={({ item }) => (
        <Link href={{ pathname: '/session/[id]', params: { id: item.id } }} asChild>
          <Pressable
            accessibilityRole="link"
            style={({ pressed }) => ({
              gap: spacing.sm,
              paddingVertical: spacing.md,
              opacity: pressed ? 0.6 : 1,
              borderBottomWidth: 0.5,
              borderColor: colors.separator,
            })}
          >
            <Text style={type.headline}>
              {item.sessionDate} / {sessionStatus(item.status)}
            </Text>
            <Text style={type.caption}>{item.workflowDisplay}</Text>
            <Text style={type.caption}>
              {item.agentCount} agents / {item.pendingApprovalCount} pending approvals
            </Text>
          </Pressable>
        </Link>
      )}
    />
  );
}
