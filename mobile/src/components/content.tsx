import { PropsWithChildren } from 'react';
import { ActivityIndicator, RefreshControl, ScrollView, Text, View } from 'react-native';
import { Button, Host } from '@expo/ui';
import { UseQueryResult } from '@tanstack/react-query';
import { colors, spacing, type } from '@/theme';

export function Screen({
  children,
  refreshing = false,
  onRefresh,
}: PropsWithChildren<{
  refreshing?: boolean;
  onRefresh?: () => void;
}>) {
  return (
    <ScrollView
      contentInsetAdjustmentBehavior="automatic"
      automaticallyAdjustKeyboardInsets
      keyboardShouldPersistTaps="handled"
      style={{ flex: 1, backgroundColor: colors.background }}
      contentContainerStyle={{ padding: spacing.md, gap: spacing.md, paddingBottom: spacing.xxl }}
      refreshControl={
        onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} /> : undefined
      }
    >
      {children}
    </ScrollView>
  );
}

export function Message({ title, children }: PropsWithChildren<{ title: string }>) {
  return (
    <View style={{ gap: spacing.sm, paddingVertical: spacing.sm }}>
      <Text selectable style={type.headline}>
        {title}
      </Text>
      {typeof children === 'string' ? (
        <Text selectable style={type.body}>
          {children}
        </Text>
      ) : (
        children
      )}
    </View>
  );
}

export function ErrorNotice({ error, onRetry }: { error: string; onRetry?: () => void }) {
  return (
    <View accessibilityRole="alert" style={{ gap: spacing.sm }}>
      <Text selectable style={[type.body, { color: colors.error }]}>
        {error}
      </Text>
      {onRetry && (
        <Host matchContents>
          <Button label="Retry" variant="outlined" onPress={onRetry} />
        </Host>
      )}
    </View>
  );
}

export function QueryState<T>({
  query,
  children,
}: {
  query: UseQueryResult<T, Error>;
  children: (data: T) => React.ReactNode;
}) {
  if (query.data === undefined) {
    if (query.error)
      return (
        <ErrorNotice
          error={query.error.message}
          onRetry={() => {
            void query.refetch();
          }}
        />
      );
    return (
      <View style={{ gap: spacing.sm }}>
        <ActivityIndicator accessibilityLabel="Loading" />
        <Text style={type.caption}>Loading from Agenti...</Text>
      </View>
    );
  }
  return (
    <>
      {query.error && (
        <ErrorNotice
          error={`Could not refresh. Showing the previous response. ${query.error.message}`}
          onRetry={() => {
            void query.refetch();
          }}
        />
      )}
      {children(query.data)}
    </>
  );
}

export function Detail({ label, value }: { label: string; value: string }) {
  return (
    <View
      style={{
        gap: spacing.xs,
        paddingVertical: spacing.sm,
        borderBottomWidth: 0.5,
        borderColor: colors.separator,
      }}
    >
      <Text style={type.caption}>{label}</Text>
      <Text selectable style={[type.body, { fontVariant: ['tabular-nums'] }]}>
        {value}
      </Text>
    </View>
  );
}
